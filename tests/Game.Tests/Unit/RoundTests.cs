using FluentAssertions;
using Game.Api.Domain;

namespace Game.Tests.Unit;

public class RoundTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static Round NewRound() => Round.Start(Guid.NewGuid(), "p1", new Money(500), T0);

    [Fact]
    public void Happy_path()
    {
        var r = NewRound();
        r.Status.Should().Be(RoundStatus.Pending);
        r.MarkDebited(new Money(850), 9500, T0.AddSeconds(1));
        r.Status.Should().Be(RoundStatus.Debited);
        r.WinCents.Should().Be(850);
        r.Settle(10350, T0.AddSeconds(2));
        r.Status.Should().Be(RoundStatus.Settled);
        r.BalanceAfterCents.Should().Be(10350);
        r.UpdatedAt.Should().Be(T0.AddSeconds(2));
    }

    [Fact]
    public void Settle_from_pending_throws() =>
        FluentActions.Invoking(() => NewRound().Settle(1, T0)).Should().Throw<InvalidOperationException>();

    [Fact]
    public void Cancel_from_debited_throws()
    {
        var r = NewRound();
        r.MarkDebited(Money.Zero, 100, T0);
        FluentActions.Invoking(() => r.Cancel(null, T0)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Flag_from_settled_throws()
    {
        var r = NewRound();
        r.MarkDebited(Money.Zero, 100, T0);
        r.Settle(100, T0);
        FluentActions.Invoking(() => r.FlagForAttention("x", T0)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Reject_cancel_flag_and_recovery()
    {
        var r = NewRound();
        r.Reject("insufficient_funds", T0);
        r.Status.Should().Be(RoundStatus.Rejected);
        r.FailureReason.Should().Be("insufficient_funds");

        var c = NewRound();
        c.Cancel(700, T0);
        c.Status.Should().Be(RoundStatus.Cancelled);
        c.BalanceAfterCents.Should().Be(700);

        var f = NewRound();
        f.FlagForAttention("boom", T0);
        f.Status.Should().Be(RoundStatus.NeedsAttention);
        f.RecordRecoveryAttempt(T0.AddSeconds(5));
        f.RecoveryAttempts.Should().Be(1);
    }
}
