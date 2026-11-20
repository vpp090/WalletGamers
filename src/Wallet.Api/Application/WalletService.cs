using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wallet.Api.Domain;
using Wallet.Api.Infrastructure;

namespace Wallet.Api.Application;

public class WalletService(WalletDbContext db, TimeProvider clock)
{
    private const string UniqueViolation = "23505";
    private const string CancelPerOriginalIndex = "ux_transactions_cancel_reference";

    public Task<WalletResult> DepositAsync(string playerId, string transactionId, Money amount, CancellationToken ct) =>
        ApplyAsync(new WriteCommand(playerId, transactionId, TransactionType.Deposit, amount.Cents, amount.Cents), ct);

    public Task<WalletResult> WithdrawAsync(string playerId, string transactionId, Money amount, CancellationToken ct) =>
        ApplyAsync(new WriteCommand(playerId, transactionId, TransactionType.Withdrawal, amount.Cents, -amount.Cents), ct);

    public Task<WalletResult> BetAsync(string playerId, string transactionId, Guid roundId, Money amount, CancellationToken ct) =>
        ApplyAsync(new WriteCommand(playerId, transactionId, TransactionType.Bet, amount.Cents, -amount.Cents, roundId,
            LockKey: transactionId,
            PreCheck: async (db, token) =>
                await db.Transactions.AsNoTracking().AnyAsync(t => t.Type == TransactionType.Cancel && t.ReferenceTransactionId == transactionId, token)
                    ? PreCheckResult.Fail(WalletError.TransactionCancelled)
                    : PreCheckResult.Ok), ct);

    public Task<WalletResult> WinAsync(string playerId, string transactionId, Guid roundId, Money amount, CancellationToken ct) =>
        ApplyAsync(new WriteCommand(playerId, transactionId, TransactionType.Win, amount.Cents, amount.Cents, roundId,
            PreCheck: async (db, token) =>
                await db.Transactions.AsNoTracking().AnyAsync(b =>
                        b.Type == TransactionType.Bet && b.PlayerId == playerId && b.RoundId == roundId
                        && db.Transactions.Any(c => c.Type == TransactionType.Cancel && c.ReferenceTransactionId == b.Id), token)
                    ? PreCheckResult.Fail(WalletError.RoundCancelled)
                    : PreCheckResult.Ok), ct);

    public Task<WalletResult> CancelAsync(string playerId, string transactionId, string originalTransactionId, CancellationToken ct) =>
        ApplyAsync(new WriteCommand(playerId, transactionId, TransactionType.Cancel, 0, 0,
            ReferenceTransactionId: originalTransactionId,
            LockKey: originalTransactionId,
            PreCheck: async (db, token) =>
            {
                var original = await db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == originalTransactionId, token);
                if (original is not null && (original.Type != TransactionType.Bet || original.PlayerId != playerId))
                    return PreCheckResult.Fail(WalletError.NotCancellable);

                // One cancel per original; a replay of the same cancel id was already handled by the idempotency lookup.
                if (await db.Transactions.AsNoTracking().AnyAsync(t => t.Type == TransactionType.Cancel && t.ReferenceTransactionId == originalTransactionId, token))
                    return PreCheckResult.Fail(WalletError.NotCancellable);

                // Original missing: tombstone (zero amount) so a late bet with that id is rejected.
                return original is null
                    ? PreCheckResult.Ok
                    : PreCheckResult.Override(original.AmountCents, original.AmountCents, original.RoundId);
            }), ct);

    public async Task<Money> GetBalanceAsync(string playerId, CancellationToken ct)
    {
        var cents = await db.Accounts.AsNoTracking()
            .Where(a => a.PlayerId == playerId)
            .Select(a => (long?)a.BalanceCents)
            .FirstOrDefaultAsync(ct);
        return new Money(cents ?? 0);
    }

    /// <summary>
    /// A single ledger write. <see cref="AmountCents"/> is the (positive) amount recorded on the ledger row;
    /// <see cref="DeltaCents"/> is the signed change applied to the balance.
    /// <see cref="PreCheck"/> runs inside the DB transaction, after the idempotency lookup and after the account row
    /// is locked, before the balance update; it may fail the write (rolled back) or override amount/delta/round.
    /// <see cref="LockKey"/>, when set, takes a transaction-scoped advisory lock so writes about the same id
    /// (bet vs cancel) serialise even across different players.
    /// </summary>
    protected internal sealed record WriteCommand(
        string PlayerId,
        string TransactionId,
        TransactionType Type,
        long AmountCents,
        long DeltaCents,
        Guid? RoundId = null,
        string? ReferenceTransactionId = null,
        Func<WalletDbContext, CancellationToken, Task<PreCheckResult>>? PreCheck = null,
        string? LockKey = null);

    protected internal sealed record PreCheckResult(WalletError? Error, long? AmountCents, long? DeltaCents, Guid? RoundId)
    {
        public static readonly PreCheckResult Ok = new(null, null, null, null);
        public static PreCheckResult Fail(WalletError error) => new(error, null, null, null);
        public static PreCheckResult Override(long amountCents, long deltaCents, Guid? roundId) => new(null, amountCents, deltaCents, roundId);
    }

    protected internal async Task<WalletResult> ApplyAsync(WriteCommand cmd, CancellationToken ct)
    {
        try
        {
            return await TryApplyAsync(cmd, ct);
        }
        catch (Exception e) when (IsUniqueViolation(e, out var constraint))
        {
            // A concurrent request with the same transaction id committed first; replay against its row.
            db.ChangeTracker.Clear();
            var existing = await FindExistingAsync(cmd.TransactionId, ct);
            if (existing is not null)
                return Replay(existing, cmd);
            return constraint == CancelPerOriginalIndex
                ? new WalletResult(cmd.TransactionId, 0, WalletError.NotCancellable)
                : throw new InvalidOperationException("Unique violation without a matching ledger row.", e);
        }
    }

    private async Task<WalletResult> TryApplyAsync(WriteCommand cmd, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var existing = await FindExistingAsync(cmd.TransactionId, ct);
        if (existing is not null)
            return Replay(existing, cmd);

        var now = clock.GetUtcNow();

        if (cmd.LockKey is not null)
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({cmd.LockKey}))", ct);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO accounts (player_id, balance_cents, updated_at) VALUES ({cmd.PlayerId}, 0, {now}) ON CONFLICT (player_id) DO NOTHING",
            ct);

        // Serialise all writes for this account so the checks below see a stable state.
        await db.Database.SqlQuery<string>($"SELECT player_id AS \"Value\" FROM accounts WHERE player_id = {cmd.PlayerId} FOR UPDATE").ToListAsync(ct);

        // Re-check idempotency now that we hold the locks: a concurrent duplicate may have committed meanwhile.
        existing = await FindExistingAsync(cmd.TransactionId, ct);
        if (existing is not null)
            return Replay(existing, cmd);

        if (cmd.PreCheck is not null)
        {
            var pre = await cmd.PreCheck(db, ct);
            if (pre.Error is { } preError)
                return new WalletResult(cmd.TransactionId, 0, preError);
            cmd = cmd with
            {
                AmountCents = pre.AmountCents ?? cmd.AmountCents,
                DeltaCents = pre.DeltaCents ?? cmd.DeltaCents,
                RoundId = pre.RoundId ?? cmd.RoundId,
            };
        }

        var updated = await db.Database.SqlQuery<long>(
            $"""
             UPDATE accounts SET balance_cents = balance_cents + {cmd.DeltaCents}, updated_at = {now}
             WHERE player_id = {cmd.PlayerId} AND balance_cents + {cmd.DeltaCents} >= 0
             RETURNING balance_cents AS "Value"
             """).ToListAsync(ct);

        if (updated.Count == 0)
        {
            await tx.RollbackAsync(ct);
            return new WalletResult(cmd.TransactionId, 0, WalletError.InsufficientFunds);
        }

        var balanceAfter = updated[0];
        db.Transactions.Add(new LedgerTransaction
        {
            Id = cmd.TransactionId,
            PlayerId = cmd.PlayerId,
            Type = cmd.Type,
            AmountCents = cmd.AmountCents,
            BalanceAfterCents = balanceAfter,
            RoundId = cmd.RoundId,
            ReferenceTransactionId = cmd.ReferenceTransactionId,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new WalletResult(cmd.TransactionId, balanceAfter, null);
    }

    private Task<LedgerTransaction?> FindExistingAsync(string transactionId, CancellationToken ct) =>
        db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == transactionId, ct);

    private static WalletResult Replay(LedgerTransaction existing, WriteCommand cmd) =>
        existing.PlayerId == cmd.PlayerId
        && existing.Type == cmd.Type
        && (cmd.Type == TransactionType.Cancel || existing.AmountCents == cmd.AmountCents)
        && existing.ReferenceTransactionId == cmd.ReferenceTransactionId
            ? new WalletResult(existing.Id, existing.BalanceAfterCents, null)
            : new WalletResult(cmd.TransactionId, 0, WalletError.IdempotencyConflict);

    private static bool IsUniqueViolation(Exception e, out string? constraint)
    {
        var pg = e as PostgresException ?? (e as DbUpdateException)?.InnerException as PostgresException;
        constraint = pg?.ConstraintName;
        return pg is { SqlState: UniqueViolation };
    }
}
