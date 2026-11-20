using Game.Api.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Game.Api.Controllers;

public static class ProblemMapping
{
    public static ObjectResult Problem(int status, string code, string title, string? detail = null, RoundResponse? round = null, string? reason = null)
    {
        var pd = new ProblemDetails { Status = status, Title = title, Detail = detail };
        pd.Extensions["code"] = code;
        if (reason is not null) pd.Extensions["reason"] = reason;
        if (round is not null)
        {
            // "status" is taken by the ProblemDetails HTTP status, so the round status is "roundStatus".
            pd.Extensions["roundId"] = round.RoundId;
            pd.Extensions["roundStatus"] = round.Status;
            pd.Extensions["bet"] = round.Bet;
            pd.Extensions["win"] = round.Win;
            pd.Extensions["outcome"] = round.Outcome;
            pd.Extensions["balance"] = round.Balance;
        }

        return new ObjectResult(pd) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }

    public static ObjectResult Validation(string detail) => Problem(400, "validation_error", "Validation error", detail);

    public static ObjectResult ToProblem(Round round)
    {
        var dto = RoundResponse.From(round);
        return round.Status switch
        {
            RoundStatus.Rejected when round.FailureReason == "insufficient_funds" =>
                Problem(422, "insufficient_funds", "Insufficient funds", round: dto),
            RoundStatus.Rejected =>
                Problem(409, "round_rejected", "Round rejected", round: dto, reason: round.FailureReason),
            RoundStatus.Cancelled => Problem(409, "round_cancelled", "Round has been cancelled", round: dto),
            RoundStatus.NeedsAttention => Problem(409, "round_needs_attention", "Round needs attention", round: dto),
            _ => Problem(503, "round_pending", "Round is still being processed", round: dto),
        };
    }
}
