using Game.Api.Domain;

namespace Game.Api.Controllers;

public record PlaceBetRequest(Guid RoundId, decimal Amount);

public record RoundResponse(Guid RoundId, string Status, decimal Bet, decimal? Win, string? Outcome, decimal? Balance)
{
    public static RoundResponse From(Round r) => new(
        r.Id,
        r.Status.ToString(),
        new Money(r.BetCents).ToDecimal(),
        r.WinCents is { } w ? new Money(w).ToDecimal() : null,
        r.WinCents is { } w2 ? (w2 > 0 ? "Win" : "Lose") : null,
        r.Status is RoundStatus.Settled or RoundStatus.Cancelled && r.BalanceAfterCents is { } b
            ? new Money(b).ToDecimal()
            : null);
}
