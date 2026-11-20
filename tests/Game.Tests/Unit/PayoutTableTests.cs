using FluentAssertions;
using Game.Api.Domain;

namespace Game.Tests.Unit;

public class PayoutTableTests
{
    private static readonly Money Bet = new(500);

    private static Payout Roll(params double[] draws)
    {
        var rng = new ScriptedRandomSource(draws);
        var payout = new PayoutTable(rng).Roll(Bet);
        rng.Remaining.Should().Be(0);
        return payout;
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.4999)]
    public void Lose_tier(double u)
    {
        var p = Roll(u);
        p.Outcome.Should().Be(Outcome.Lose);
        p.Win.Cents.Should().Be(0);
    }

    [Theory]
    [InlineData(0.5, 0.0, 1000)]
    [InlineData(0.8999, 0.99999, 1)]
    [InlineData(0.5, 0.15, 850)]
    [InlineData(0.9, 0.0, 5000)]
    [InlineData(0.9999, 0.99999, 1001)]
    public void Win_tiers(double u, double v, long expected)
    {
        var p = Roll(u, v);
        p.Outcome.Should().Be(Outcome.Win);
        p.Win.Cents.Should().Be(expected);
    }

    [Fact]
    public void Distribution_matches_table()
    {
        const int n = 100_000;
        var table = new PayoutTable(new CryptoRandomSource());
        int lose = 0, low = 0, high = 0;
        for (var i = 0; i < n; i++)
        {
            var p = table.Roll(Bet);
            if (p.Outcome == Outcome.Lose) { lose++; p.Win.Cents.Should().Be(0); }
            else if (p.Win.Cents <= 1000) { low++; p.Win.Cents.Should().BeInRange(1, 1000); }
            else { high++; p.Win.Cents.Should().BeInRange(1001, 5000); }
        }

        ((double)lose / n).Should().BeApproximately(0.5, 0.01);
        ((double)low / n).Should().BeApproximately(0.4, 0.01);
        ((double)high / n).Should().BeApproximately(0.1, 0.01);
    }
}
