using FluentAssertions;
using Wallet.Api.Domain;

namespace Wallet.Tests.Unit;

public class MoneyTests
{
    [Fact]
    public void TryFromDecimal_rejects_decimal_extremes_without_throwing()
    {
        Money.TryFromDecimal(decimal.MaxValue, out _).Should().BeFalse();
        Money.TryFromDecimal(decimal.MinValue, out _).Should().BeFalse();
    }

    [Fact]
    public void TryFromDecimal_whole_amount()
    {
        Money.TryFromDecimal(10m, out var m).Should().BeTrue();
        m.Cents.Should().Be(1000);
    }

    [Fact]
    public void TryFromDecimal_half()
    {
        Money.TryFromDecimal(8.5m, out var m).Should().BeTrue();
        m.Cents.Should().Be(850);
    }

    [Fact]
    public void TryFromDecimal_one_cent()
    {
        Money.TryFromDecimal(0.01m, out var m).Should().BeTrue();
        m.Cents.Should().Be(1);
    }

    [Fact]
    public void TryFromDecimal_rejects_more_than_two_decimals()
    {
        Money.TryFromDecimal(10.005m, out _).Should().BeFalse();
    }

    [Fact]
    public void TryFromDecimal_accepts_trailing_zero_scale()
    {
        Money.TryFromDecimal(1.10m, out var m).Should().BeTrue();
        m.Cents.Should().Be(110);
    }

    [Fact]
    public void ToDecimal_converts_cents()
    {
        new Money(1350).ToDecimal().Should().Be(13.50m);
    }

    [Fact]
    public void Subtraction_works()
    {
        (new Money(1000) - new Money(500)).Cents.Should().Be(500);
    }

    [Fact]
    public void Addition_comparison_zero_positive()
    {
        (new Money(1) + new Money(2)).Cents.Should().Be(3);
        (new Money(2) > new Money(1)).Should().BeTrue();
        (new Money(1) < new Money(2)).Should().BeTrue();
        (new Money(2) >= new Money(2)).Should().BeTrue();
        (new Money(2) <= new Money(2)).Should().BeTrue();
        Money.Zero.IsPositive.Should().BeFalse();
        new Money(1).IsPositive.Should().BeTrue();
    }
}
