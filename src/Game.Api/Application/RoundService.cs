using Game.Api.Domain;
using Game.Api.Infrastructure;
using Game.Api.Infrastructure.Wallet;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Game.Api.Application;

public record PlayResult(Round? Round, bool IdempotencyConflict);

public class RoundService(GameDbContext db, IWalletClient wallet, PayoutTable payouts, TimeProvider clock)
{
    public async Task<PlayResult> PlayAsync(string playerId, Guid roundId, Money bet, CancellationToken ct)
    {
        Round round;
        while (true)
        {
            var existing = await db.Rounds.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roundId, ct);
            if (existing is not null)
            {
                return existing.PlayerId != playerId || existing.BetCents != bet.Cents
                    ? new PlayResult(null, true)
                    : new PlayResult(existing, false);
            }

            round = Round.Start(roundId, playerId, bet, clock.GetUtcNow());
            db.Rounds.Add(round);
            try
            {
                await db.SaveChangesAsync(ct);
                break;
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.ChangeTracker.Clear();
            }
        }

        var debit = await wallet.DebitAsync(playerId, WalletClient.BetId(roundId), roundId, bet, ct);
        switch (debit.Status)
        {
            case WalletCallStatus.Unavailable:
                return new PlayResult(round, false);
            case WalletCallStatus.Rejected when debit.ErrorCode == "transaction_cancelled":
                round.Cancel(null, clock.GetUtcNow());
                return new PlayResult(await SaveAsync(round, CancellationToken.None), false);
            case WalletCallStatus.Rejected:
                round.Reject(debit.ErrorCode ?? "wallet_rejected", clock.GetUtcNow());
                return new PlayResult(await SaveAsync(round, CancellationToken.None), false);
        }

        var payout = payouts.Roll(bet);
        var balance = debit.BalanceCents!.Value;
        round.MarkDebited(payout.Win, balance, clock.GetUtcNow());
        if (!payout.Win.IsPositive)
            round.Settle(balance, clock.GetUtcNow());
        var saved = await SaveAsync(round, CancellationToken.None);
        if (round.Status != RoundStatus.Debited)
            return new PlayResult(saved, false);

        var credit = await wallet.CreditAsync(playerId, WalletClient.WinId(roundId), roundId, new Money(round.WinCents!.Value), ct);
        switch (credit.Status)
        {
            case WalletCallStatus.Success:
                round.Settle(credit.BalanceCents!.Value, clock.GetUtcNow());
                break;
            case WalletCallStatus.Rejected:
                round.FlagForAttention(credit.ErrorCode ?? "wallet_rejected", clock.GetUtcNow());
                break;
            default:
                return new PlayResult(round, false);
        }

        return new PlayResult(await SaveAsync(round, CancellationToken.None), false);
    }

    public Task<Round?> GetAsync(string playerId, Guid roundId, CancellationToken ct) =>
        db.Rounds.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roundId && r.PlayerId == playerId, ct);

    // Saves the round; if another writer moved it first, reloads and returns the current state.
    private async Task<Round> SaveAsync(Round round, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return round;
        }
        catch (DbUpdateConcurrencyException e)
        {
            var entry = e.Entries.Single();
            await entry.ReloadAsync(ct);
            return (Round)entry.Entity;
        }
    }
}
