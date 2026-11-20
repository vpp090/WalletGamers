using System.Net.Http.Json;
using System.Text.Json;
using Game.Api.Domain;
using Microsoft.Extensions.Configuration;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Game.Api.Infrastructure.Wallet;

public sealed class WalletClient : IWalletClient
{
    private readonly HttpClient _http;

    public WalletClient(HttpClient http) => _http = http;

    public static string BetId(Guid roundId) => $"{roundId}:bet";
    public static string WinId(Guid roundId) => $"{roundId}:win";
    public static string CancelId(Guid roundId) => $"{roundId}:cancel";

    public Task<WalletCallResult> DebitAsync(string playerId, string transactionId, Guid roundId, Money amount, CancellationToken ct) =>
        PostAsync(playerId, "bets", new WalletRoundMoneyRequest(transactionId, roundId, amount.ToDecimal()), ct);

    public Task<WalletCallResult> CreditAsync(string playerId, string transactionId, Guid roundId, Money amount, CancellationToken ct) =>
        PostAsync(playerId, "wins", new WalletRoundMoneyRequest(transactionId, roundId, amount.ToDecimal()), ct);

    public Task<WalletCallResult> CancelAsync(string playerId, string transactionId, string originalTransactionId, CancellationToken ct) =>
        PostAsync(playerId, "cancellations", new WalletCancelRequest(transactionId, originalTransactionId), ct);

    private async Task<WalletCallResult> PostAsync<T>(string playerId, string action, T body, CancellationToken ct)
    {
        try
        {
            var path = $"api/players/{Uri.EscapeDataString(playerId)}/{action}";
            using var response = await _http.PostAsJsonAsync(path, body, ct);

            if (response.IsSuccessStatusCode)
            {
                var ok = await response.Content.ReadFromJsonAsync<WalletTransactionResponse>(ct)
                    ?? throw new InvalidOperationException("Wallet returned an empty success body.");
                if (!Money.TryFromDecimal(ok.Balance, out var balance))
                    throw new InvalidOperationException($"Wallet returned an invalid balance: {ok.Balance}.");
                return WalletCallResult.Success(balance.Cents);
            }

            if ((int)response.StatusCode is >= 400 and < 500 and not (408 or 429))
                return WalletCallResult.Rejected(await ReadCodeAsync(response, ct));

            return WalletCallResult.Unavailable();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException
                                      or BrokenCircuitException or TimeoutRejectedException)
        {
            return WalletCallResult.Unavailable();
        }
    }

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("code", out var code)
                   && code.ValueKind == JsonValueKind.String
                ? code.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
