using FluentAssertions;
using System.Net;
using System.Text.Json;
using Game.Api.Domain;
using Game.Api.Infrastructure.Wallet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Tests.Unit;

public class WalletClientTests
{
    private static WalletClient Create(StubHttpHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://wallet.test/") });

    [Fact]
    public async Task Debit_success_maps_balance_and_request()
    {
        var roundId = Guid.NewGuid();
        var handler = StubHttpHandler.Json(HttpStatusCode.OK, """{"transactionId":"x","balance":13.5}""");

        var result = await Create(handler).DebitAsync("p1", WalletClient.BetId(roundId), roundId, new Money(500), default);

        result.Status.Should().Be(WalletCallStatus.Success);
        result.BalanceCents.Should().Be(1350);
        var (req, body) = handler.Calls.Single();
        req.Method.Should().Be(HttpMethod.Post);
        req.RequestUri!.AbsolutePath.Should().Be("/api/players/p1/bets");
        using var doc = JsonDocument.Parse(body!);
        doc.RootElement.GetProperty("transactionId").GetString().Should().Be($"{roundId}:bet");
        doc.RootElement.GetProperty("amount").GetDecimal().Should().Be(5m);
    }

    [Fact]
    public async Task Credit_posts_to_wins()
    {
        var handler = StubHttpHandler.Json(HttpStatusCode.OK, """{"transactionId":"x","balance":1}""");
        await Create(handler).CreditAsync("p1", "t", Guid.NewGuid(), new Money(100), default);
        handler.Calls.Single().Request.RequestUri!.AbsolutePath.Should().Be("/api/players/p1/wins");
    }

    [Fact]
    public async Task Cancel_posts_to_cancellations()
    {
        var handler = StubHttpHandler.Json(HttpStatusCode.OK, """{"transactionId":"x","balance":2}""");
        var result = await Create(handler).CancelAsync("p1", "c", "o", default);
        result.BalanceCents.Should().Be(200);
        var (req, body) = handler.Calls.Single();
        req.RequestUri!.AbsolutePath.Should().Be("/api/players/p1/cancellations");
        using var doc = JsonDocument.Parse(body!);
        doc.RootElement.GetProperty("originalTransactionId").GetString().Should().Be("o");
    }

    [Fact]
    public async Task Rejected_carries_problem_code()
    {
        var handler = StubHttpHandler.Json((HttpStatusCode)422, """{"title":"x","status":422,"code":"insufficient_funds"}""");
        var result = await Create(handler).DebitAsync("p1", "t", Guid.NewGuid(), new Money(500), default);
        result.Status.Should().Be(WalletCallStatus.Rejected);
        result.ErrorCode.Should().Be("insufficient_funds");
    }

    [Fact]
    public async Task ServerError_is_unavailable()
    {
        var handler = StubHttpHandler.Json(HttpStatusCode.ServiceUnavailable, "{}");
        var result = await Create(handler).DebitAsync("p1", "t", Guid.NewGuid(), new Money(500), default);
        result.Status.Should().Be(WalletCallStatus.Unavailable);
    }

    [Fact]
    public async Task HttpRequestException_is_unavailable()
    {
        var handler = new StubHttpHandler((_, _) => throw new HttpRequestException("boom"));
        var result = await Create(handler).DebitAsync("p1", "t", Guid.NewGuid(), new Money(500), default);
        result.Status.Should().Be(WalletCallStatus.Unavailable);
    }

    [Fact]
    public async Task Timeout_not_from_caller_is_unavailable()
    {
        var handler = new StubHttpHandler((_, _) => throw new TaskCanceledException("timeout"));
        var result = await Create(handler).DebitAsync("p1", "t", Guid.NewGuid(), new Money(500), default);
        result.Status.Should().Be(WalletCallStatus.Unavailable);
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = StubHttpHandler.Json(HttpStatusCode.OK, "{}");
        var act = () => Create(handler).DebitAsync("p1", "t", Guid.NewGuid(), new Money(500), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Transaction_id_helpers()
    {
        var id = Guid.NewGuid();
        WalletClient.BetId(id).Should().Be($"{id}:bet");
        WalletClient.WinId(id).Should().Be($"{id}:win");
        WalletClient.CancelId(id).Should().Be($"{id}:cancel");
    }

    [Fact]
    public void AddWalletClient_resolves_with_valid_config()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Wallet:BaseUrl"] = "http://wallet.test/"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWalletClient(config);
        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IWalletClient>().Should().NotBeNull();
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    public async Task Timeout_and_throttle_statuses_are_unavailable_not_rejected(int status)
    {
        var handler = StubHttpHandler.Json((HttpStatusCode)status, """{"code":"x"}""");
        var result = await Create(handler).DebitAsync("p1", "t", Guid.NewGuid(), new Money(500), default);
        result.Status.Should().Be(WalletCallStatus.Unavailable);
    }
}
