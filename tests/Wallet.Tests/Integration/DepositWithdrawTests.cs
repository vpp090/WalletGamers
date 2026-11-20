using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Wallet.Tests.Integration;

[Collection("postgres")]
public sealed class DepositWithdrawTests : IDisposable
{
    private sealed record BalanceDto(decimal Balance);
    private sealed record Tx(string TransactionId, decimal Balance);
    private sealed record Problem(string? Code);

    private readonly WalletApiFactory _factory;
    private readonly HttpClient _http;

    public DepositWithdrawTests(PostgresFixture pg)
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

    private async Task<decimal> GetBalance(string player)
    {
        var r = await _http.GetAsync($"api/players/{player}/balance");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await r.Content.ReadFromJsonAsync<BalanceDto>())!.Balance;
    }

    private static async Task<string?> CodeOf(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<Problem>())?.Code;

    [Fact]
    public async Task New_player_balance_is_zero()
    {
        (await GetBalance(NewPlayer())).Should().Be(0m);
    }

    [Fact]
    public async Task Deposit_returns_new_balance()
    {
        var r = await Deposit(NewPlayer(), NewTx(), 10m);
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.Content.ReadFromJsonAsync<Tx>())!.Balance.Should().Be(10m);
    }

    [Fact]
    public async Task Withdraw_reduces_balance()
    {
        var p = NewPlayer();
        await Deposit(p, NewTx(), 10m);
        var r = await Withdraw(p, NewTx(), 3.50m);
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.Content.ReadFromJsonAsync<Tx>())!.Balance.Should().Be(6.50m);
    }

    [Fact]
    public async Task Withdraw_over_balance_is_insufficient_funds()
    {
        var p = NewPlayer();
        await Deposit(p, NewTx(), 5m);
        var r = await Withdraw(p, NewTx(), 6m);
        r.StatusCode.Should().Be((HttpStatusCode)422);
        (await CodeOf(r)).Should().Be("insufficient_funds");
        (await GetBalance(p)).Should().Be(5m);
    }

    [Fact]
    public async Task Withdraw_for_unknown_player_is_insufficient_funds()
    {
        var p = NewPlayer();
        var r = await Withdraw(p, NewTx(), 1m);
        r.StatusCode.Should().Be((HttpStatusCode)422);
        (await GetBalance(p)).Should().Be(0m);
    }

    [Fact]
    public async Task Replayed_deposit_moves_money_once()
    {
        var p = NewPlayer();
        var tx = NewTx();
        var a = await Deposit(p, tx, 10m);
        var b = await Deposit(p, tx, 10m);
        a.StatusCode.Should().Be(HttpStatusCode.OK);
        b.StatusCode.Should().Be(HttpStatusCode.OK);
        (await b.Content.ReadFromJsonAsync<Tx>())!.Balance.Should().Be(10m);
        (await GetBalance(p)).Should().Be(10m);
    }

    [Fact]
    public async Task Replay_with_different_amount_is_conflict()
    {
        var p = NewPlayer();
        var tx = NewTx();
        await Deposit(p, tx, 10m);
        var r = await Deposit(p, tx, 20m);
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(r)).Should().Be("idempotency_conflict");
        (await GetBalance(p)).Should().Be(10m);
    }

    [Fact]
    public async Task Replay_with_different_player_is_conflict()
    {
        var a = NewPlayer();
        var b = NewPlayer();
        var tx = NewTx();
        await Deposit(a, tx, 10m);
        var r = await Deposit(b, tx, 10m);
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(r)).Should().Be("idempotency_conflict");
        (await GetBalance(b)).Should().Be(0m);
    }

    [Theory]
    [InlineData(10.005)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Invalid_amount_is_validation_error(double amount)
    {
        var r = await Deposit(NewPlayer(), NewTx(), (decimal)amount);
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(r)).Should().Be("validation_error");
    }

    [Fact]
    public async Task Missing_transaction_id_is_validation_error()
    {
        var r = await _http.PostAsJsonAsync($"api/players/{NewPlayer()}/deposits", new { amount = 5m });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(r)).Should().Be("validation_error");
    }

    [Fact]
    public async Task Concurrent_replays_of_same_transaction_move_money_once()
    {
        var p = NewPlayer();
        var tx = NewTx();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Deposit(p, tx, 10m)));
        results.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        (await GetBalance(p)).Should().Be(10m);
    }

    [Fact]
    public async Task Deposit_above_one_million_is_validation_error()
    {
        var r = await Deposit(NewPlayer(), NewTx(), 1000000.01m);
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(r)).Should().Be("validation_error");
    }

    [Fact]
    public async Task Deposit_of_exactly_one_million_is_accepted()
    {
        var r = await Deposit(NewPlayer(), NewTx(), 1000000m);
        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Absurdly_large_deposit_is_validation_error()
    {
        var r = await _http.PostAsJsonAsync($"api/players/{NewPlayer()}/deposits",
            new { transactionId = NewTx(), amount = 79228162514264337593543950335m });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(r)).Should().Be("validation_error");
    }
}
