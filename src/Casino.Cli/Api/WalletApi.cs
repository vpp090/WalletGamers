using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Casino.Cli.Api;

public class WalletApi(HttpClient http, Func<TimeSpan, Task>? delay = null) : IWalletApi
{
    private const int MaxAttempts = 3;
    private readonly Func<TimeSpan, Task> _delay = delay ?? (t => Task.Delay(t));

    public Task<ApiResult<decimal>> DepositAsync(string player, decimal amount) => MoneyAsync(player, "deposits", amount);

    public Task<ApiResult<decimal>> WithdrawAsync(string player, decimal amount) => MoneyAsync(player, "withdrawals", amount);

    public async Task<ApiResult<decimal>> GetBalanceAsync(string player)
    {
        var path = $"api/players/{Uri.EscapeDataString(player)}/balance";
        for (var attempt = 1; ; attempt++)
        {
            var result = Map(await Http.TrySendAsync(http, () => new HttpRequestMessage(HttpMethod.Get, path)));
            if (result.Status != ApiStatus.Unavailable || attempt >= MaxAttempts) return result;
            await _delay(TimeSpan.FromMilliseconds(200 * attempt));
        }
    }

    // One transaction id per user action; reused for this call's own retries so the Wallet de-duplicates.
    private async Task<ApiResult<decimal>> MoneyAsync(string player, string kind, decimal amount)
    {
        var path = $"api/players/{Uri.EscapeDataString(player)}/{kind}";
        var transactionId = Guid.NewGuid().ToString();
        for (var attempt = 1; ; attempt++)
        {
            var result = Map(await Http.TrySendAsync(http, () => new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(new { transactionId, amount }, options: Http.Json)
            }));
            if (result.Status != ApiStatus.Unavailable || attempt >= MaxAttempts) return result;
            await _delay(TimeSpan.FromMilliseconds(200 * attempt));
        }
    }

    private static ApiResult<decimal> Map((HttpStatusCode Status, JsonElement Body)? response)
    {
        if (response is not var (status, body)) return Http.Unavailable<decimal>();
        var code = (int)status;
        if (code is >= 200 and < 300)
            return Http.Decimal(body, "balance") is { } b
                ? new(ApiStatus.Ok, b, null, null)
                : Http.Unavailable<decimal>();
        if (code is >= 400 and < 500 and not (408 or 429))
            return new(ApiStatus.BusinessError, default, Http.String(body, "code") ?? $"http_{code}", Http.String(body, "detail"));
        return Http.Unavailable<decimal>();
    }
}
