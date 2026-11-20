using Game.Api.Application;
using Game.Api.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Game.Api.Controllers;

[ApiController]
[Route("api/players/{playerId}/rounds")]
public class RoundsController(RoundService rounds) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Play(string playerId, PlaceBetRequest request, CancellationToken ct)
    {
        if (request.RoundId == Guid.Empty)
            return ProblemMapping.Validation("roundId is required.");
        if (!BetRules.TryCreate(request.Amount, out var bet, out var error))
            return ProblemMapping.Validation(error!);

        var result = await rounds.PlayAsync(playerId, request.RoundId, bet, ct);
        if (result.IdempotencyConflict)
            return ProblemMapping.Problem(409, "idempotency_conflict", "Round id already used with different details");

        var round = result.Round!;
        return round.Status == RoundStatus.Settled ? Ok(RoundResponse.From(round)) : ProblemMapping.ToProblem(round);
    }

    [HttpGet("{roundId:guid}")]
    public async Task<IActionResult> Get(string playerId, Guid roundId, CancellationToken ct)
    {
        var round = await rounds.GetAsync(playerId, roundId, ct);
        return round is null
            ? ProblemMapping.Problem(404, "round_not_found", "Round not found")
            : Ok(RoundResponse.From(round));
    }
}
