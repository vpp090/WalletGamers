using System.Net;
using System.Text.Json;

namespace Casino.Cli.Api;

public enum ApiStatus { Ok, BusinessError, Pending, Unavailable }

public record ApiResult<T>(ApiStatus Status, T? Value, string? ErrorCode, string? Detail);

public record RoundView(Guid RoundId, string Status, decimal Bet, decimal? Win, string? Outcome, decimal? Balance);

internal static class Http
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Sends a request, converting connection failures and timeouts to a null response.</summary>
    public static async Task<(HttpStatusCode Status, JsonElement Body)?> TrySendAsync(HttpClient client, Func<HttpRequestMessage> request)
    {
        try
        {
            using var message = request();
            using var response = await client.SendAsync(message);
            var text = await response.Content.ReadAsStringAsync();
            JsonElement body = default;
            if (!string.IsNullOrWhiteSpace(text))
            {
                try { body = JsonDocument.Parse(text).RootElement.Clone(); }
                catch (JsonException) { /* non-JSON body: treated as empty */ }
            }

            return (response.StatusCode, body);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
        {
            return null;
        }
    }

    public static string? String(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    public static decimal? Decimal(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number
            ? p.GetDecimal()
            : null;

    public static ApiResult<T> Unavailable<T>() => new(ApiStatus.Unavailable, default, null, null);
}
