using FluentAssertions;
using Game.Api.Domain;

namespace Game.Tests.Unit;

public class BetRulesTests
{
    [Theory]
    [InlineData("1", 100)]
    [InlineData("10", 1000)]
    [InlineData("5.25", 525)]
    public void Valid(string amount, long cents)
    {
        BetRules.TryCreate(decimal.Parse(amount), out var bet, out var error).Should().BeTrue();
        bet.Cents.Should().Be(cents);
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("0.99")]
    [InlineData("10.01")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("5.255")]
    public void Invalid(string amount)
    {
        BetRules.TryCreate(decimal.Parse(amount), out _, out var error).Should().BeFalse();
        error.Should().NotBeNull();
    }

    [Fact]
    public void Decimal_extremes_are_invalid_without_throwing()
    {
        BetRules.TryCreate(decimal.MaxValue, out _, out var e1).Should().BeFalse();
        e1.Should().NotBeNull();
        BetRules.TryCreate(decimal.MinValue, out _, out var e2).Should().BeFalse();
        e2.Should().NotBeNull();
    }
}
