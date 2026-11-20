using System.Net.Http.Json;
using FluentAssertions;
using Game.Api.Application;
using Game.Api.Infrastructure;
using Game.Api.Infrastructure.Wallet;
using Game.Tests.Unit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Game.Tests.Integration;

[Collection("postgres")]
public sealed class RecoveryTests : IDisposable
{
    private sealed record RoundDto(Guid RoundId, string Status, decimal Bet, decimal? Win, string? Outcome, decimal? Balance);

    private readonly PostgresFixture _pg;
    private readonly FakeWalletClient _wallet = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
    private GameApiFactory? _factory;
    private HttpClient? _http;

    public RecoveryTests(PostgresFixture pg) => _pg = pg;

    public void Dispose()
    {
        _http?.Dispose();
        _factory?.Dispose();
    }

    // Each test gets its own database so rounds left over by other tests are never claimed.
    private void Start(params double[] rolls)
    {
        var cs = new NpgsqlConnectionStringBuilder(_pg.ConnectionString) { Database = $"recovery_{Guid.NewGuid():N}" };
        _factory = new GameApiFactory(cs.ConnectionString, _wallet, new ScriptedRandomSource(rolls), _clock);
        _http = _factory.CreateClient();
    }

    private async Task<Guid> Play(string player, decimal amount)
    {
        var round = Guid.NewGuid();
        await _http!.PostAsJsonAsync($"api/players/{player}/rounds", new { roundId = round, amount });
        return round;
    }

    private async Task<RoundDto> Get(string player, Guid round) =>
        (await _http!.GetFromJsonAsync<RoundDto>($"api/players/{player}/rounds/{round}"))!;

    private async Task<int> RunOnce()
    {
        using var scope = _factory!.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RecoveryService>().RunOnceAsync(CancellationToken.None);
    }

    private async Task<Game.Api.Domain.Round> Load(Guid round)
    {
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        return (await db.Rounds.FindAsync(round))!;
    }

    [Fact]
    public async Task Pending_round_is_cancelled_after_grace_period()
    {
        Start();
        _wallet.DebitResult = WalletCallResult.Unavailable();
        var p = Guid.NewGuid().ToString();
        var round = await Play(p, 5m);

        _clock.Advance(TimeSpan.FromSeconds(10));
        (await RunOnce()).Should().Be(0);
        (await Get(p, round)).Status.Should().Be("Pending");
        _wallet.Cancels.Should().BeEmpty();

        _clock.Advance(TimeSpan.FromSeconds(21));
        _wallet.CancelResult = WalletCallResult.Success(1000);
        (await RunOnce()).Should().Be(1);

        var dto = await Get(p, round);
        dto.Status.Should().Be("Cancelled");
        dto.Balance.Should().Be(10m);
        _wallet.Cancels.Should().ContainSingle();
        _wallet.Cancels[0].TransactionId.Should().Be($"{round}:cancel");
        _wallet.Cancels[0].Original.Should().Be($"{round}:bet");
    }

    [Fact]
    public async Task Debited_round_is_credited_with_stored_amount()
    {
        Start(0.5, 0.15);
        _wallet.DebitResult = WalletCallResult.Success(500);
        _wallet.CreditResult = WalletCallResult.Unavailable();
        var p = Guid.NewGuid().ToString();
        var round = await Play(p, 5m);
        (await Get(p, round)).Status.Should().Be("Debited");

        _clock.Advance(TimeSpan.FromSeconds(31));
        _wallet.CreditResult = WalletCallResult.Success(1350);
        (await RunOnce()).Should().Be(1);

        var dto = await Get(p, round);
        dto.Status.Should().Be("Settled");
        dto.Balance.Should().Be(13.50m);
        _wallet.Credits.Should().HaveCount(2);
        _wallet.Credits[1].AmountCents.Should().Be(850);
        _wallet.Credits[1].TransactionId.Should().Be($"{round}:win");
        _wallet.Debits.Should().ContainSingle();
    }

    [Fact]
    public async Task Unavailable_wallet_keeps_round_and_counts_attempt()
    {
        Start();
        _wallet.DebitResult = WalletCallResult.Unavailable();
        _wallet.CancelResult = WalletCallResult.Unavailable();
        var p = Guid.NewGuid().ToString();
        var round = await Play(p, 5m);

        _clock.Advance(TimeSpan.FromSeconds(31));
        (await RunOnce()).Should().Be(0);

        (await Get(p, round)).Status.Should().Be("Pending");
        (await Load(round)).RecoveryAttempts.Should().Be(1);
    }

    [Fact]
    public async Task Wallet_rejection_flags_round_for_attention()
    {
        Start(0.5, 0.15);
        _wallet.DebitResult = WalletCallResult.Success(500);
        _wallet.CreditResult = WalletCallResult.Unavailable();
        var p = Guid.NewGuid().ToString();
        var round = await Play(p, 5m);

        _clock.Advance(TimeSpan.FromSeconds(31));
        _wallet.CreditResult = WalletCallResult.Rejected("round_cancelled");
        (await RunOnce()).Should().Be(1);

        (await Get(p, round)).Status.Should().Be("NeedsAttention");
        (await Load(round)).FailureReason.Should().Be("round_cancelled");

        var calls = _wallet.TotalCalls;
        _clock.Advance(TimeSpan.FromSeconds(31));
        (await RunOnce()).Should().Be(0);
        _wallet.TotalCalls.Should().Be(calls);
    }

    [Fact]
    public async Task Settled_rounds_are_ignored()
    {
        Start(0.1);
        _wallet.DebitResult = WalletCallResult.Success(500);
        var p = Guid.NewGuid().ToString();
        var round = await Play(p, 5m);
        (await Get(p, round)).Status.Should().Be("Settled");
        var calls = _wallet.TotalCalls;

        _clock.Advance(TimeSpan.FromMinutes(5));
        (await RunOnce()).Should().Be(0);

        _wallet.TotalCalls.Should().Be(calls);
    }

    [Fact]
    public async Task Throwing_round_does_not_block_rest_of_batch()
    {
        Start();
        _wallet.DebitResult = WalletCallResult.Unavailable();
        var bad = Guid.NewGuid().ToString();
        var good = Guid.NewGuid().ToString();
        var badRound = await Play(bad, 5m);
        _clock.Advance(TimeSpan.FromSeconds(1));
        var goodRound = await Play(good, 5m);

        _clock.Advance(TimeSpan.FromSeconds(31));
        _wallet.ThrowForPlayers.Add(bad);
        _wallet.CancelResult = WalletCallResult.Success(1000);
        (await RunOnce()).Should().Be(1);

        (await Get(good, goodRound)).Status.Should().Be("Cancelled");
        var failed = await Load(badRound);
        failed.Status.Should().Be(Game.Api.Domain.RoundStatus.Pending);
        failed.RecoveryAttempts.Should().Be(1);
    }
}
