using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wallet.Api.Domain;
using Wallet.Api.Infrastructure;

namespace System.Tests;

public sealed class SystemTests(SystemFixture f) : IClassFixture<SystemFixture>, IAsyncLifetime
{
    public Task InitializeAsync()
    {
        f.Fault.Reset();
        f.Random.Clear();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string NewPlayer() => "p-" + Guid.NewGuid().ToString("N");

    private async Task<decimal> DepositAsync(string player, decimal amount)
    {
        var r = await f.Wallet.PostAsJsonAsync($"api/players/{player}/deposits",
            new { transactionId = Guid.NewGuid().ToString(), amount });
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("balance").GetDecimal();
    }

    private async Task<decimal> BalanceAsync(string player)
    {
        var r = await f.Wallet.GetFromJsonAsync<JsonElement>($"api/players/{player}/balance");
        return r.GetProperty("balance").GetDecimal();
    }

    private Task<HttpResponseMessage> BetAsync(string player, Guid roundId, decimal amount) =>
        f.Game.PostAsJsonAsync($"api/players/{player}/rounds", new { roundId, amount });

    private Task<JsonElement> GetRoundAsync(string player, Guid roundId) =>
        f.Game.GetFromJsonAsync<JsonElement>($"api/players/{player}/rounds/{roundId}");

    private async Task<List<LedgerTransaction>> LedgerAsync(Guid roundId)
    {
        await using var scope = f.WalletServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        return await db.Transactions.Where(t => t.RoundId == roundId).ToListAsync();
    }

    [Fact]
    public async Task Brief_example_session_end_to_end()
    {
        var player = NewPlayer();
        (await DepositAsync(player, 10m)).Should().Be(10m);

        f.Random.Enqueue(0.5, 0.15);
        var roundId = Guid.NewGuid();
        var bet = await BetAsync(player, roundId, 5m);
        bet.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await bet.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("win").GetDecimal().Should().Be(8.50m);
        body.GetProperty("balance").GetDecimal().Should().Be(13.50m);

        var w = await f.Wallet.PostAsJsonAsync($"api/players/{player}/withdrawals",
            new { transactionId = Guid.NewGuid().ToString(), amount = 13.50m });
        w.StatusCode.Should().Be(HttpStatusCode.OK);
        (await BalanceAsync(player)).Should().Be(0m);
    }

    [Fact]
    public async Task Credit_failure_settles_after_recovery()
    {
        var player = NewPlayer();
        await DepositAsync(player, 10m);

        f.Random.Enqueue(0.5, 0.15);
        f.Fault.FailWhen = r => r.RequestUri!.AbsolutePath.EndsWith("/wins");
        var roundId = Guid.NewGuid();
        var bet = await BetAsync(player, roundId, 5m);

        bet.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var problem = await bet.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("round_pending");
        problem.GetProperty("win").GetDecimal().Should().Be(8.50m);
        (await BalanceAsync(player)).Should().Be(5m);

        f.Fault.Reset();
        f.Clock.Advance(TimeSpan.FromSeconds(31));
        (await f.RunRecoveryAsync()).Should().BeGreaterThanOrEqualTo(1);

        var round = await GetRoundAsync(player, roundId);
        round.GetProperty("status").GetString().Should().Be("Settled");
        round.GetProperty("balance").GetDecimal().Should().Be(13.50m);
        (await BalanceAsync(player)).Should().Be(13.50m);

        var ledger = await LedgerAsync(roundId);
        ledger.Count(t => t.Type == TransactionType.Bet).Should().Be(1);
        ledger.Count(t => t.Type == TransactionType.Win).Should().Be(1);
    }

    [Fact]
    public async Task Debit_lost_response_is_refunded_by_recovery()
    {
        var player = NewPlayer();
        await DepositAsync(player, 10m);

        f.Random.Enqueue(0.5, 0.15);
        f.Fault.ForwardThenFail = true;
        f.Fault.FailWhen = r => r.RequestUri!.AbsolutePath.EndsWith("/bets");
        var roundId = Guid.NewGuid();
        var bet = await BetAsync(player, roundId, 5m);

        bet.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await GetRoundAsync(player, roundId)).GetProperty("status").GetString().Should().Be("Pending");
        (await BalanceAsync(player)).Should().Be(5m);

        f.Fault.Reset();
        f.Clock.Advance(TimeSpan.FromSeconds(31));
        (await f.RunRecoveryAsync()).Should().BeGreaterThanOrEqualTo(1);

        (await GetRoundAsync(player, roundId)).GetProperty("status").GetString().Should().Be("Cancelled");
        (await BalanceAsync(player)).Should().Be(10m);
    }
}
