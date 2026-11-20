using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Game.Api.Infrastructure.Wallet;
using Game.Tests.Unit;

namespace Game.Tests.Integration;

[Collection("postgres")]
public sealed class RoundsApiTests : IDisposable
{
    private sealed record RoundDto(Guid RoundId, string Status, decimal Bet, decimal? Win, string? Outcome, decimal? Balance);
    private sealed record Problem(string? Code, string? Reason, Guid? RoundId, string? RoundStatus, decimal? Bet, decimal? Win, string? Outcome, decimal? Balance);

    private readonly PostgresFixture _pg;
    private readonly FakeWalletClient _wallet = new();
    private GameApiFactory? _factory;
    private HttpClient? _http;
    private ScriptedRandomSource? _random;

    public RoundsApiTests(PostgresFixture pg) => _pg = pg;

    public void Dispose()
    {
        _http?.Dispose();
        _factory?.Dispose();
    }

    private void Start(params double[] rolls)
    {
        _random = new ScriptedRandomSource(rolls);
        _factory = new GameApiFactory(_pg.ConnectionString, _wallet, _random);
        _http = _factory.CreateClient();
    }

    private static string NewPlayer() => Guid.NewGuid().ToString();

    private Task<HttpResponseMessage> Play(string player, Guid round, decimal amount) =>
        _http!.PostAsJsonAsync($"api/players/{player}/rounds", new { roundId = round, amount });

    private Task<HttpResponseMessage> Get(string player, Guid round) =>
        _http!.GetAsync($"api/players/{player}/rounds/{round}");

    private static async Task<T> Body<T>(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<T>())!;

    [Fact]
    public async Task Winning_round_settles()
    {
        Start(0.5, 0.15);
        _wallet.DebitResult = WalletCallResult.Success(500);
        _wallet.CreditResult = WalletCallResult.Success(1350);
        var round = Guid.NewGuid();

        var r = await Play(NewPlayer(), round, 5m);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await Body<RoundDto>(r);
        body.Status.Should().Be("Settled");
        body.Win.Should().Be(8.50m);
        body.Balance.Should().Be(13.50m);
        body.Outcome.Should().Be("Win");
        _wallet.Credits.Should().ContainSingle();
        _wallet.Credits[0].AmountCents.Should().Be(850);
        _wallet.Credits[0].TransactionId.Should().Be($"{round}:win");
        _wallet.Debits[0].TransactionId.Should().Be($"{round}:bet");
    }

    [Fact]
    public async Task Losing_round_settles_without_credit()
    {
        Start(0.1);
        _wallet.DebitResult = WalletCallResult.Success(500);

        var r = await Play(NewPlayer(), Guid.NewGuid(), 5m);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await Body<RoundDto>(r);
        body.Win.Should().Be(0m);
        body.Outcome.Should().Be("Lose");
        body.Status.Should().Be("Settled");
        body.Balance.Should().Be(5m);
        _wallet.Credits.Should().BeEmpty();
    }

    [Fact]
    public async Task Insufficient_funds_rejects()
    {
        Start();
        _wallet.DebitResult = WalletCallResult.Rejected("insufficient_funds");
        var p = NewPlayer();
        var round = Guid.NewGuid();

        var r = await Play(p, round, 5m);

        r.StatusCode.Should().Be((HttpStatusCode)422);
        (await Body<Problem>(r)).Code.Should().Be("insufficient_funds");
        var g = await Get(p, round);
        g.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Body<RoundDto>(g)).Status.Should().Be("Rejected");
    }

    [Fact]
    public async Task Debit_unavailable_leaves_round_pending()
    {
        Start();
        _wallet.DebitResult = WalletCallResult.Unavailable();
        var p = NewPlayer();
        var round = Guid.NewGuid();

        var r = await Play(p, round, 5m);

        r.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Body<Problem>(r)).Code.Should().Be("round_pending");
        var g = await Body<RoundDto>(await Get(p, round));
        g.Status.Should().Be("Pending");
        g.Win.Should().BeNull();
    }

    [Fact]
    public async Task Credit_unavailable_leaves_round_debited_with_known_outcome()
    {
        Start(0.5, 0.15);
        _wallet.DebitResult = WalletCallResult.Success(500);
        _wallet.CreditResult = WalletCallResult.Unavailable();
        var p = NewPlayer();
        var round = Guid.NewGuid();

        var r = await Play(p, round, 5m);

        r.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var problem = await Body<Problem>(r);
        problem.Code.Should().Be("round_pending");
        problem.Win.Should().Be(8.50m);
        problem.Outcome.Should().Be("Win");
        problem.RoundId.Should().Be(round);
        (await Body<RoundDto>(await Get(p, round))).Status.Should().Be("Debited");
    }

    [Fact]
    public async Task Replay_returns_stored_result_without_wallet_calls()
    {
        Start(0.5, 0.15);
        _wallet.DebitResult = WalletCallResult.Success(500);
        _wallet.CreditResult = WalletCallResult.Success(1350);
        var p = NewPlayer();
        var round = Guid.NewGuid();

        var first = await Body<RoundDto>(await Play(p, round, 5m));
        var calls = _wallet.TotalCalls;
        var remaining = _random!.Remaining;

        var again = await Play(p, round, 5m);

        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Body<RoundDto>(again)).Should().Be(first);
        _wallet.TotalCalls.Should().Be(calls);
        _random.Remaining.Should().Be(remaining);
    }

    [Fact]
    public async Task Replay_of_pending_round_makes_no_wallet_call()
    {
        Start();
        _wallet.DebitResult = WalletCallResult.Unavailable();
        var p = NewPlayer();
        var round = Guid.NewGuid();
        await Play(p, round, 5m);
        var calls = _wallet.TotalCalls;

        var again = await Play(p, round, 5m);

        again.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _wallet.TotalCalls.Should().Be(calls);
    }

    [Fact]
    public async Task Replay_with_different_amount_is_conflict()
    {
        Start(0.1);
        _wallet.DebitResult = WalletCallResult.Success(500);
        var p = NewPlayer();
        var round = Guid.NewGuid();
        await Play(p, round, 5m);

        var diffAmount = await Play(p, round, 6m);
        var diffPlayer = await Play(NewPlayer(), round, 5m);

        diffAmount.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Body<Problem>(diffAmount)).Code.Should().Be("idempotency_conflict");
        diffPlayer.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Body<Problem>(diffPlayer)).Code.Should().Be("idempotency_conflict");
    }

    [Theory]
    [InlineData(0.99)]
    [InlineData(10.01)]
    [InlineData(5.255)]
    public async Task Bet_out_of_range_is_validation_error(double amount)
    {
        Start();

        var r = await Play(NewPlayer(), Guid.NewGuid(), (decimal)amount);

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Body<Problem>(r)).Code.Should().Be("validation_error");
        _wallet.TotalCalls.Should().Be(0);
    }

    [Fact]
    public async Task Get_round_of_other_player_is_404()
    {
        Start(0.1);
        _wallet.DebitResult = WalletCallResult.Success(500);
        var round = Guid.NewGuid();
        await Play(NewPlayer(), round, 5m);

        (await Get(NewPlayer(), round)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var missing = await Get(NewPlayer(), Guid.NewGuid());
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Body<Problem>(missing)).Code.Should().Be("round_not_found");
    }

    [Fact]
    public async Task Debit_rejected_for_other_reason_returns_round_rejected_with_reason()
    {
        Start();
        _wallet.DebitResult = WalletCallResult.Rejected("validation_error");

        var r = await Play(NewPlayer(), Guid.NewGuid(), 5m);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await Body<Problem>(r);
        body.Code.Should().Be("round_rejected");
        body.Reason.Should().Be("validation_error");
    }

    [Fact]
    public async Task Debit_rejected_as_cancelled_returns_round_cancelled()
    {
        Start();
        _wallet.DebitResult = WalletCallResult.Rejected("transaction_cancelled");

        var r = await Play(NewPlayer(), Guid.NewGuid(), 5m);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Body<Problem>(r)).Code.Should().Be("round_cancelled");
    }

    [Fact]
    public async Task Credit_rejected_returns_round_needs_attention()
    {
        Start(0.5, 0.15);
        _wallet.DebitResult = WalletCallResult.Success(500);
        _wallet.CreditResult = WalletCallResult.Rejected("round_cancelled");
        var p = NewPlayer();
        var round = Guid.NewGuid();

        var r = await Play(p, round, 5m);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Body<Problem>(r)).Code.Should().Be("round_needs_attention");
        (await Body<RoundDto>(await Get(p, round))).Status.Should().Be("NeedsAttention");
    }

    [Fact]
    public async Task Parallel_posts_with_same_round_id_debit_once()
    {
        Start(0.1, 0.1, 0.1, 0.1);
        _wallet.DebitResult = WalletCallResult.Success(500);
        var p = NewPlayer();
        var round = Guid.NewGuid();

        await Task.WhenAll(Play(p, round, 5m), Play(p, round, 5m));

        _wallet.Debits.Should().ContainSingle();
        (4 - _random!.Remaining).Should().BeLessThanOrEqualTo(1);
    }
}
