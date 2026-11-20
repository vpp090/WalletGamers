using Casino.Cli;
using Casino.Cli.Api;
using FluentAssertions;

namespace Casino.Cli.Tests;

public class CasinoSessionTests
{
    private static readonly Guid Id = Guid.NewGuid();
    private static ApiResult<decimal> Bal(decimal b) => new(ApiStatus.Ok, b, null, null);
    private static ApiResult<RoundView> Round(ApiStatus s, string status, decimal? win, decimal? bal, string? outcome = null) =>
        new(s, new RoundView(Id, status, 5m, win, outcome, bal), null, null);

    private static CasinoSession Session(FakeWallet w, FakeGame g) => new("alice", w, g, _ => Task.CompletedTask);

    [Fact]
    public async Task Brief_example_session()
    {
        var w = new FakeWallet();
        var g = new FakeGame();
        var s = Session(w, g);

        w.DepositResult = Bal(10m);
        (await s.ExecuteAsync(new Deposit(10m))).Should().Be("Your deposit of $10 was successful. Your current balance is: $10");

        g.BetResult = Round(ApiStatus.Ok, "Settled", 8.50m, 13.50m, "Win");
        (await s.ExecuteAsync(new Bet(5m))).Should().Be("Congrats - you won $8.50! Your current balance is: $13.50");

        w.WithdrawResult = Bal(0m);
        (await s.ExecuteAsync(new Withdraw(13.50m))).Should().Be("Your withdrawal of $13.50 was successful. Your current balance is: $0");

        (await s.ExecuteAsync(new Exit())).Should().Be("Thank you for playing! Hope to see you again soon.");
    }

    [Fact]
    public async Task Losing_bet_message()
    {
        var g = new FakeGame { BetResult = Round(ApiStatus.Ok, "Settled", 0m, 5m, "Lose") };
        (await Session(new FakeWallet(), g).ExecuteAsync(new Bet(5m))).Should().Be("No luck this time! Your current balance is: $5");
    }

    [Fact]
    public async Task Pending_win_polls_until_settled()
    {
        var g = new FakeGame { BetResult = Round(ApiStatus.Pending, "Debited", 8.50m, null, "Win") };
        g.GetResults.Enqueue(Round(ApiStatus.Ok, "Debited", 8.50m, null, "Win"));
        g.GetResults.Enqueue(Round(ApiStatus.Ok, "Settled", 8.50m, 13.50m, "Win"));

        var output = await Session(new FakeWallet(), g).ExecuteAsync(new Bet(5m));

        output.Should().Contain("You won $8.50 - it will be credited shortly.");
        output.Should().Contain("Your current balance is: $13.50");
        g.GetCalls.Should().Be(2);
    }

    [Fact]
    public async Task Pending_unresolved_after_polling_reports_wallet_balance()
    {
        var g = new FakeGame { BetResult = Round(ApiStatus.Pending, "Pending", null, null) };
        g.GetDefault = Round(ApiStatus.Pending, "Pending", null, null);
        var w = new FakeWallet { BalanceResult = Bal(5m) };

        var output = await Session(w, g).ExecuteAsync(new Bet(5m));

        g.GetCalls.Should().Be(12);
        output.Split('\n').Last().Should().Be("Your round will be settled automatically. Your current balance is: $5");
        output.Should().Contain("Your bet is being processed; any charge will be refunded if it can't complete.");
    }

    [Fact]
    public async Task Unavailable_service_prints_friendly_message()
    {
        var w = new FakeWallet { DepositResult = new(ApiStatus.Unavailable, 0m, null, null) };
        (await Session(w, new FakeGame()).ExecuteAsync(new Deposit(10m)))
            .Should().Be("Sorry, the service is unavailable right now. Please try again.");
    }

    [Fact]
    public async Task Insufficient_funds_message()
    {
        var g = new FakeGame { BetResult = new(ApiStatus.BusinessError, null, "insufficient_funds", null) };
        var w = new FakeWallet { BalanceResult = Bal(0m) };
        (await Session(w, g).ExecuteAsync(new Bet(5m))).Should().Be("Insufficient funds. Your current balance is: $0");
    }

    [Fact]
    public async Task Cancelled_bet_reports_refund()
    {
        var g = new FakeGame
        {
            BetResult = new(ApiStatus.BusinessError, new RoundView(Id, "Cancelled", 5m, null, null, 7m), "round_cancelled", null)
        };
        (await Session(new FakeWallet(), g).ExecuteAsync(new Bet(5m)))
            .Should().Be("Your bet could not be completed and was refunded. Your current balance is: $7");
    }

    [Fact]
    public async Task Bet_retries_reuse_round_id_on_unavailable()
    {
        var g = new FakeGame { BetResult = new(ApiStatus.Unavailable, null, null, null) };
        var output = await Session(new FakeWallet(), g).ExecuteAsync(new Bet(5m));
        g.BetIds.Should().HaveCount(3).And.OnlyContain(i => i == g.BetIds[0]);
        output.Should().Be("Sorry, the service is unavailable right now. Please try again.");
    }

    [Fact]
    public async Task Out_of_range_bet_message()
    {
        var g = new FakeGame { BetResult = new(ApiStatus.BusinessError, null, "validation_error", "bad") };
        (await Session(new FakeWallet(), g).ExecuteAsync(new Bet(50m))).Should().Be("Bets must be between $1 and $10.");
    }

    [Theory]
    [InlineData("Rejected", "Sorry, that could not be completed. Your current balance is: $5")]
    [InlineData("NeedsAttention", "Your round has been flagged for review. Your current balance is: $5")]
    public async Task Polling_stops_on_terminal_rejected_and_needs_attention(string status, string expected)
    {
        var g = new FakeGame { BetResult = Round(ApiStatus.Pending, "Pending", null, null) };
        g.GetResults.Enqueue(Round(ApiStatus.Ok, status, null, null));
        var w = new FakeWallet { BalanceResult = Bal(5m) };

        var output = await Session(w, g).ExecuteAsync(new Bet(5m));

        g.GetCalls.Should().Be(1);
        output.Split('\n').Last().Should().Be(expected);
    }

    [Fact]
    public async Task Round_rejected_code_prints_generic_message()
    {
        var g = new FakeGame { BetResult = new(ApiStatus.BusinessError, null, "round_rejected", null) };
        var w = new FakeWallet { BalanceResult = Bal(5m) };
        (await Session(w, g).ExecuteAsync(new Bet(5m))).Should().Be("Sorry, that could not be completed. Your current balance is: $5");
    }

    [Fact]
    public async Task Validation_error_from_game_prints_bet_range()
    {
        var g = new FakeGame { BetResult = new(ApiStatus.BusinessError, null, "validation_error", null) };
        (await Session(new FakeWallet(), g).ExecuteAsync(new Bet(50m))).Should().Be("Bets must be between $1 and $10.");
    }
}
