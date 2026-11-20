using System.Net;
using System.Text;
using Casino.Cli;
using Casino.Cli.Api;
using FluentAssertions;

namespace Casino.Cli.Tests;

public class ApiMappingTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body, string mediaType = "application/json") =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    private static HttpClient Client(StubHandler h) => new(h) { BaseAddress = new Uri("http://localhost") };

    [Fact]
    public async Task Game_503_round_pending_maps_round_status_extension()
    {
        var id = Guid.NewGuid();
        var body = $$"""{"status":503,"code":"round_pending","roundId":"{{id}}","roundStatus":"Debited","bet":5.00,"win":8.50,"outcome":"Win","balance":null}""";
        var api = new GameApi(Client(new StubHandler(_ => Json(HttpStatusCode.ServiceUnavailable, body, "application/problem+json"))));

        var r = await api.PlaceBetAsync("alice", id, 5m);

        r.Status.Should().Be(ApiStatus.Pending);
        r.Value.Should().Be(new RoundView(id, "Debited", 5m, 8.50m, "Win", null));
    }

    [Fact]
    public async Task Game_200_maps_round()
    {
        var id = Guid.NewGuid();
        var body = $$"""{"roundId":"{{id}}","status":"Settled","bet":5.00,"win":0,"outcome":"Lose","balance":5.00}""";
        var api = new GameApi(Client(new StubHandler(_ => Json(HttpStatusCode.OK, body))));

        var r = await api.GetRoundAsync("alice", id);

        r.Status.Should().Be(ApiStatus.Ok);
        r.Value!.Status.Should().Be("Settled");
        r.Value.Balance.Should().Be(5m);
    }

    [Fact]
    public async Task Game_409_round_cancelled_is_business_error_with_balance()
    {
        var id = Guid.NewGuid();
        var body = $$"""{"status":409,"code":"round_cancelled","roundId":"{{id}}","roundStatus":"Cancelled","bet":5.00,"win":null,"outcome":null,"balance":7.00}""";
        var api = new GameApi(Client(new StubHandler(_ => Json(HttpStatusCode.Conflict, body, "application/problem+json"))));

        var r = await api.PlaceBetAsync("alice", id, 5m);

        r.Status.Should().Be(ApiStatus.BusinessError);
        r.ErrorCode.Should().Be("round_cancelled");
        r.Value!.Balance.Should().Be(7m);
    }

    [Fact]
    public async Task Game_other_5xx_and_connection_failure_are_unavailable()
    {
        var a = new GameApi(Client(new StubHandler(_ => Json(HttpStatusCode.InternalServerError, "{}"))));
        (await a.GetRoundAsync("alice", Guid.NewGuid())).Status.Should().Be(ApiStatus.Unavailable);

        var b = new GameApi(Client(new StubHandler(_ => throw new HttpRequestException("down"))));
        (await b.PlaceBetAsync("alice", Guid.NewGuid(), 5m)).Status.Should().Be(ApiStatus.Unavailable);
    }

    [Fact]
    public async Task Wallet_deposit_ok_returns_balance()
    {
        var api = new WalletApi(Client(new StubHandler(_ => Json(HttpStatusCode.OK, """{"transactionId":"x","balance":10.00}"""))), _ => Task.CompletedTask);
        var r = await api.DepositAsync("alice", 10m);
        r.Status.Should().Be(ApiStatus.Ok);
        r.Value.Should().Be(10m);
    }

    [Fact]
    public async Task Wallet_422_is_business_error_with_code()
    {
        var api = new WalletApi(Client(new StubHandler(_ => Json(HttpStatusCode.UnprocessableEntity, """{"status":422,"code":"insufficient_funds"}""", "application/problem+json"))), _ => Task.CompletedTask);
        var r = await api.WithdrawAsync("alice", 10m);
        r.Status.Should().Be(ApiStatus.BusinessError);
        r.ErrorCode.Should().Be("insufficient_funds");
    }

    [Fact]
    public async Task Wallet_retries_unavailable_with_same_transaction_id()
    {
        var calls = 0;
        var h = new StubHandler(_ => ++calls < 3
            ? Json(HttpStatusCode.ServiceUnavailable, "{}")
            : Json(HttpStatusCode.OK, """{"transactionId":"x","balance":3.00}"""));
        var api = new WalletApi(Client(h), _ => Task.CompletedTask);

        var r = await api.DepositAsync("alice", 3m);

        r.Status.Should().Be(ApiStatus.Ok);
        h.Bodies.Should().HaveCount(3).And.OnlyContain(b => b == h.Bodies[0]);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    public async Task Wallet_408_and_429_are_unavailable(int status)
    {
        var api = new WalletApi(Client(new StubHandler(_ => Json((HttpStatusCode)status, """{"code":"x"}"""))), _ => Task.CompletedTask);
        (await api.DepositAsync("alice", 5m)).Status.Should().Be(ApiStatus.Unavailable);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    public async Task Game_408_and_429_are_unavailable(int status)
    {
        var api = new GameApi(Client(new StubHandler(_ => Json((HttpStatusCode)status, """{"code":"x"}"""))));
        (await api.PlaceBetAsync("alice", Guid.NewGuid(), 5m)).Status.Should().Be(ApiStatus.Unavailable);
    }
}
