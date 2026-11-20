namespace Game.Api.Domain;

public class Round
{
    private Round() { }

    public Guid Id { get; private set; }
    public string PlayerId { get; private set; } = "";
    public long BetCents { get; private set; }
    public long? WinCents { get; private set; }
    public RoundStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public long? BalanceAfterCents { get; private set; }
    public int RecoveryAttempts { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Round Start(Guid id, string playerId, Money bet, DateTimeOffset now) => new()
    {
        Id = id,
        PlayerId = playerId,
        BetCents = bet.Cents,
        Status = RoundStatus.Pending,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void MarkDebited(Money win, long balanceAfterDebitCents, DateTimeOffset now)
    {
        Require(RoundStatus.Pending);
        WinCents = win.Cents;
        BalanceAfterCents = balanceAfterDebitCents;
        Transition(RoundStatus.Debited, now);
    }

    public void Settle(long balanceCents, DateTimeOffset now)
    {
        Require(RoundStatus.Debited);
        BalanceAfterCents = balanceCents;
        Transition(RoundStatus.Settled, now);
    }

    public void Reject(string reason, DateTimeOffset now)
    {
        Require(RoundStatus.Pending);
        FailureReason = reason;
        Transition(RoundStatus.Rejected, now);
    }

    public void Cancel(long? balanceCents, DateTimeOffset now)
    {
        Require(RoundStatus.Pending);
        BalanceAfterCents = balanceCents;
        Transition(RoundStatus.Cancelled, now);
    }

    public void FlagForAttention(string reason, DateTimeOffset now)
    {
        Require(RoundStatus.Pending, RoundStatus.Debited);
        FailureReason = reason;
        Transition(RoundStatus.NeedsAttention, now);
    }

    public void RecordRecoveryAttempt(DateTimeOffset now)
    {
        RecoveryAttempts++;
        UpdatedAt = now;
    }

    private void Require(params RoundStatus[] allowed)
    {
        if (Array.IndexOf(allowed, Status) < 0)
            throw new InvalidOperationException($"Invalid round transition from {Status}.");
    }

    private void Transition(RoundStatus next, DateTimeOffset now)
    {
        Status = next;
        UpdatedAt = now;
    }
}
