using Game.Api.Domain;
using Game.Api.Infrastructure;
using Game.Api.Infrastructure.Wallet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Game.Api.Application;

public class RecoveryService(
    GameDbContext db,
    IWalletClient wallet,
    TimeProvider clock,
    IOptions<RecoveryOptions> options,
    ILogger<RecoveryService> logger)
{
    private readonly RecoveryOptions _options = options.Value;

    // Returns the number of rounds moved to a terminal state.
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow() - _options.GracePeriod;
        var batch = _options.BatchSize;

        // Rows stay locked (SKIP LOCKED keeps other instances off them) until the batch is done.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rounds = await db.Rounds
            .FromSqlInterpolated($"""
                SELECT * FROM rounds
                WHERE status IN ('Pending', 'Debited') AND updated_at < {cutoff}
                ORDER BY updated_at
                LIMIT {batch}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        var resolved = 0;
        foreach (var round in rounds)
        {
            if (await RecoverAsync(round, ct))
                resolved++;
        }

        await tx.CommitAsync(CancellationToken.None);
        return resolved;
    }

    private async Task<bool> RecoverAsync(Round round, CancellationToken ct)
    {
        bool terminal;
        try
        {
            terminal = await ApplyWalletOutcomeAsync(round, ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            // One bad round must not block the batch: treat it as unavailable so its updated_at advances.
            logger.LogError(e, "Recovery of round {RoundId} for player {PlayerId} failed unexpectedly",
                round.Id, round.PlayerId);
            terminal = false;
            RecordAttempt(round, clock.GetUtcNow());
        }

        try
        {
            // The wallet call may already have moved money, so persist regardless of cancellation.
            await db.SaveChangesAsync(CancellationToken.None);
            return terminal;
        }
        catch (DbUpdateConcurrencyException)
        {
            // The request path settled it first; nothing left to do.
            db.Entry(round).State = EntityState.Detached;
            return false;
        }
    }

    private void RecordAttempt(Round round, DateTimeOffset now)
    {
        round.RecordRecoveryAttempt(now);
        if (round.RecoveryAttempts >= _options.WarnAfterAttempts)
            logger.LogWarning("Round {RoundId} for player {PlayerId} still unrecovered after {Attempts} attempts",
                round.Id, round.PlayerId, round.RecoveryAttempts);
    }

    private async Task<bool> ApplyWalletOutcomeAsync(Round round, CancellationToken ct)
    {
        var result = round.Status == RoundStatus.Pending
            ? await wallet.CancelAsync(round.PlayerId, WalletClient.CancelId(round.Id), WalletClient.BetId(round.Id), ct)
            : await wallet.CreditAsync(round.PlayerId, WalletClient.WinId(round.Id), round.Id, new Money(round.WinCents!.Value), ct);

        var now = clock.GetUtcNow();
        var terminal = true;
        switch (result.Status)
        {
            case WalletCallStatus.Success when round.Status == RoundStatus.Pending:
                round.Cancel(result.BalanceCents, now);
                break;
            case WalletCallStatus.Success:
                round.Settle(result.BalanceCents!.Value, now);
                break;
            case WalletCallStatus.Rejected:
                var code = result.ErrorCode ?? "wallet_rejected";
                round.FlagForAttention(code, now);
                logger.LogError("Recovery of round {RoundId} for player {PlayerId} was rejected by the wallet: {Code}",
                    round.Id, round.PlayerId, code);
                break;
            default:
                terminal = false;
                RecordAttempt(round, now);
                break;
        }

        return terminal;
    }
}
