using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Wallet.Tests.Integration;

[Collection("postgres")]
public sealed class BetWinCancelTests : IDisposable
{
    private sealed record BalanceDto(decimal Balance);
    private sealed record Tx(string TransactionId, decimal Balance);
    private sealed record Problem(string? Code);

    private readonly WalletApiFactory _factory;
    private readonly HttpClient _http;

    public BetWinCancelTests(PostgresFixture pg)
    {
        _factory = new WalletApiFactory(pg.ConnectionString);
        _http = _factory.CreateClient();
    }

    public void Dispose()
    {
        _http.Dispose();
        _factory.Dispose();
    }

    private static string NewPlayer() => Guid.NewGuid().ToString();
    private static string NewTx() => Guid.NewGuid().ToString();

    private Task<HttpResponseMessage> Deposit(string player, string txId, decimal amount) =>
        _http.PostAsJsonAsync($"api/players/{player}/deposits", new { transactionId = txId, amount });

    private Task<HttpResponseMessage> Withdraw(string player, string txId, decimal amount) =>
        _http.PostAsJsonAsync($"api/players/{player}/withdrawals", new { transactionId = txId, amount });

    private Task<HttpResponseMessage> Bet(string player, string txId, Guid round, decimal amount) =>
        _http.PostAsJsonAsync($"api/players/{player}/bets", new { transactionId = txId, roundId = round, amount });

    private Task<HttpResponseMessage> Win(string player, string txId, Guid round, decimal amount) =>
        _http.PostAsJsonAsync($"api/players/{player}/wins", new { transactionId = txId, roundId = round, amount });

    private Task<HttpResponseMessage> Cancel(string player, string txId, string original) =>
        _http.PostAsJsonAsync($"api/players/{player}/cancellations", new { transactionId = txId, originalTransactionId = original });

    private async Task<decimal> GetBalance(string player)
    {
        var r = await _http.GetAsync($"api/players/{player}/balance");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await r.Content.ReadFromJsonAsync<BalanceDto>())!.Balance;
    }

    private static async Task<string?> CodeOf(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<Problem>())?.Code;

    [Fact]
    public async Task Bet_debits_and_win_credits()
    {
        var p = NewPlayer();
        var round = Guid.NewGuid();
        await Deposit(p, NewTx(), 10m);

        var bet = await Bet(p, NewTx(), round, 5m);
        bet.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetBalance(p)).Should().Be(5m);

        var win = await Win(p, NewTx(), round, 8.50m);
        win.StatusCode.Should().Be(HttpStatusCode.OK);
        (await win.Content.ReadFromJsonAsync<Tx>())!.Balance.Should().Be(13.50m);
        (await GetBalance(p)).Should().Be(13.50m);
    }

    [Fact]
    public async Task Bet_over_balance_is_insufficient_funds()
    {
        var p = NewPlayer();
        await Deposit(p, NewTx(), 3m);
        var r = await Bet(p, NewTx(), Guid.NewGuid(), 5m);
        r.StatusCode.Should().Be((HttpStatusCode)422);
        (await CodeOf(r)).Should().Be("insufficient_funds");
        (await GetBalance(p)).Should().Be(3m);
    }

    [Fact]
    public async Task Bet_for_unknown_player_is_insufficient_funds()
    {
        var p = NewPlayer();
        var r = await Bet(p, NewTx(), Guid.NewGuid(), 1m);
        r.StatusCode.Should().Be((HttpStatusCode)422);
        (await GetBalance(p)).Should().Be(0m);
    }

    [Fact]
    public async Task Concurrent_bets_never_overdraw()
    {
        var p = NewPlayer();
        await Deposit(p, NewTx(), 10m);
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Bet(p, NewTx(), Guid.NewGuid(), 1m)));
        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(10);
        results.Count(r => r.StatusCode == (HttpStatusCode)422).Should().Be(10);
        (await GetBalance(p)).Should().Be(0m);
    }

    [Fact]
    public async Task Concurrent_withdrawals_never_overdraw()
    {
        var p = NewPlayer();
        await Deposit(p, NewTx(), 10m);
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Withdraw(p, NewTx(), 3m)));
        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(3);
        results.Count(r => r.StatusCode == (HttpStatusCode)422).Should().Be(7);
        (await GetBalance(p)).Should().Be(1m);
    }

    [Fact]
    public async Task Cancel_after_bet_refunds_once()
    {
        var p = NewPlayer();
        var betTx = NewTx();
        var cancelTx = NewTx();
        await Deposit(p, NewTx(), 10m);
        await Bet(p, betTx, Guid.NewGuid(), 5m);

        var a = await Cancel(p, cancelTx, betTx);
        var b = await Cancel(p, cancelTx, betTx);
        a.StatusCode.Should().Be(HttpStatusCode.OK);
        b.StatusCode.Should().Be(HttpStatusCode.OK);
        (await a.Content.ReadFromJsonAsync<Tx>())!.Balance.Should().Be(10m);
        (await b.Content.ReadFromJsonAsync<Tx>())!.Balance.Should().Be(10m);
        (await GetBalance(p)).Should().Be(10m);
    }

    [Fact]
    public async Task Second_cancel_with_different_id_is_not_cancellable()
    {
        var p = NewPlayer();
        var betTx = NewTx();
        await Deposit(p, NewTx(), 10m);
        await Bet(p, betTx, Guid.NewGuid(), 5m);
        (await Cancel(p, NewTx(), betTx)).StatusCode.Should().Be(HttpStatusCode.OK);

        var r = await Cancel(p, NewTx(), betTx);
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(r)).Should().Be("not_cancellable");
        (await GetBalance(p)).Should().Be(10m);
    }

    [Fact]
    public async Task Concurrent_cancels_with_different_ids_refund_once()
    {
        var p = NewPlayer();
        var betTx = NewTx();
        await Deposit(p, NewTx(), 10m);
        await Bet(p, betTx, Guid.NewGuid(), 5m);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Cancel(p, NewTx(), betTx)));
        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(5);
        (await GetBalance(p)).Should().Be(10m);
    }

    [Fact]
    public async Task Concurrent_cancels_with_same_id_all_succeed()
    {
        for (var i = 0; i < 5; i++)
        {
            var p = NewPlayer();
            var betTx = NewTx();
            var cancelTx = NewTx();
            await Deposit(p, NewTx(), 10m);
            await Bet(p, betTx, Guid.NewGuid(), 5m);

            var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Cancel(p, cancelTx, betTx)));
            results.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
            foreach (var r in results)
                (await r.Content.ReadFromJsonAsync<Tx>())!.Balance.Should().Be(10m);
            (await GetBalance(p)).Should().Be(10m);
        }
    }

    [Fact]
    public async Task Concurrent_duplicate_bets_that_drain_balance_all_succeed()
    {
        for (var i = 0; i < 5; i++)
        {
            var p = NewPlayer();
            var betTx = NewTx();
            var round = Guid.NewGuid();
            await Deposit(p, NewTx(), 5m);

            var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Bet(p, betTx, round, 5m)));
            results.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
            foreach (var r in results)
                (await r.Content.ReadFromJsonAsync<Tx>())!.Balance.Should().Be(0m);
            (await GetBalance(p)).Should().Be(0m);
        }
    }

    [Fact]
    public async Task Concurrent_duplicate_withdrawals_that_drain_balance_all_succeed()
    {
        for (var i = 0; i < 5; i++)
        {
            var p = NewPlayer();
            var tx = NewTx();
            await Deposit(p, NewTx(), 5m);

            var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Withdraw(p, tx, 5m)));
            results.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
            (await GetBalance(p)).Should().Be(0m);
        }
    }

    [Fact]
    public async Task Cancel_before_bet_blocks_late_bet()
    {
        var p = NewPlayer();
        var cancel = await Cancel(p, NewTx(), "r1:bet:" + p);
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetBalance(p)).Should().Be(0m);

        await Deposit(p, NewTx(), 10m);
        var late = await Bet(p, "r1:bet:" + p, Guid.NewGuid(), 5m);
        late.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(late)).Should().Be("transaction_cancelled");
        (await GetBalance(p)).Should().Be(10m);
    }

    [Fact]
    public async Task Concurrent_bet_and_cancel_of_same_id_never_both_apply()
    {
        for (var i = 0; i < 10; i++)
        {
            var p = NewPlayer();
            var betTx = NewTx();
            await Deposit(p, NewTx(), 10m);

            var bet = Bet(p, betTx, Guid.NewGuid(), 5m);
            var cancel = Cancel(p, NewTx(), betTx);
            await Task.WhenAll(bet, cancel);

            // Either bet then cancel (refunded) or cancel then bet (rejected): money is never lost.
            (await GetBalance(p)).Should().Be(10m);
        }
    }

    [Fact]
    public async Task Cancel_of_deposit_is_not_cancellable()
    {
        var p = NewPlayer();
        var dep = NewTx();
        await Deposit(p, dep, 10m);
        var r = await Cancel(p, NewTx(), dep);
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(r)).Should().Be("not_cancellable");
        (await GetBalance(p)).Should().Be(10m);
    }

    [Fact]
    public async Task Cancel_of_another_players_bet_is_not_cancellable()
    {
        var a = NewPlayer();
        var b = NewPlayer();
        var betTx = NewTx();
        await Deposit(a, NewTx(), 10m);
        await Bet(a, betTx, Guid.NewGuid(), 5m);

        var r = await Cancel(b, NewTx(), betTx);
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(r)).Should().Be("not_cancellable");
        (await GetBalance(a)).Should().Be(5m);
    }

    [Fact]
    public async Task Win_after_cancelled_bet_is_rejected()
    {
        var p = NewPlayer();
        var round = Guid.NewGuid();
        var betTx = NewTx();
        await Deposit(p, NewTx(), 10m);
        await Bet(p, betTx, round, 5m);
        await Cancel(p, NewTx(), betTx);

        var win = await Win(p, NewTx(), round, 8m);
        win.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(win)).Should().Be("round_cancelled");
        (await GetBalance(p)).Should().Be(10m);
    }
}
