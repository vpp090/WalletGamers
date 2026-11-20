using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Casino.Cli.Api;

public class GameApi(HttpClient http) : IGameApi
{
    public async Task<ApiResult<RoundView>> PlaceBetAsync(string player, Guid roundId, decimal amount) =>
        Map(await Http.TrySendAsync(http, () => new HttpRequestMessage(HttpMethod.Post, $"api/players/{Uri.EscapeDataString(player)}/rounds")
        {
            Content = JsonContent.Create(new { roundId, amount }, options: Http.Json)
        }));

    public async Task<ApiResult<RoundView>> GetRoundAsync(string player, Guid roundId) =>
        Map(await Http.TrySendAsync(http, () => new HttpRequestMessage(HttpMethod.Get, $"api/players/{Uri.EscapeDataString(player)}/rounds/{roundId}")));

    private static ApiResult<RoundView> Map((HttpStatusCode Status, JsonElement Body)? response)
    {
        if (response is not var (status, body)) return Http.Unavailable<RoundView>();
        var code = (int)status;

        if (code is >= 200 and < 300)
            return ToView(body, "status") is { } v ? new(ApiStatus.Ok, v, null, null) : Http.Unavailable<RoundView>();

        var errorCode = Http.String(body, "code");
        var detail = Http.String(body, "detail");

        // On ProblemDetails "status" is the HTTP status, so the round status travels as "roundStatus" (ruling R7).
        if (code == 503 && errorCode == "round_pending")
            return new(ApiStatus.Pending, ToView(body, "roundStatus"), errorCode, detail);

        if (code is >= 400 and < 500 and not (408 or 429))
            return new(ApiStatus.BusinessError, ToView(body, "roundStatus"), errorCode ?? $"http_{code}", detail);

        return Http.Unavailable<RoundView>();
    }

    private static RoundView? ToView(JsonElement body, string statusField)
    {
        if (body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty("roundId", out var id) || !id.TryGetGuid(out var roundId)
            || Http.String(body, statusField) is not { } status)
            return null;

        return new RoundView(roundId, status, Http.Decimal(body, "bet") ?? 0m, Http.Decimal(body, "win"),
            Http.String(body, "outcome"), Http.Decimal(body, "balance"));
    }
}
