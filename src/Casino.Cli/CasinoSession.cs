using Casino.Cli.Api;

namespace Casino.Cli;

public class CasinoSession(
    string player,
    IWalletApi wallet,
    IGameApi game,
    Func<TimeSpan, Task> delay,
    int pollAttempts = 12,
    TimeSpan? pollInterval = null)
{
    private const int BetAttempts = 3;
    private readonly TimeSpan _pollInterval = pollInterval ?? TimeSpan.FromSeconds(5);

    public const string UnavailableMessage = "Sorry, the service is unavailable right now. Please try again.";

    public const string HelpText =
        "Commands: deposit <amount>, withdraw <amount>, bet <amount>, balance, help, exit.";

    public async Task<string> ExecuteAsync(Command command) => command switch
    {
        Deposit d => await MoneyAsync(await wallet.DepositAsync(player, d.Amount), "deposit", d.Amount),
        Withdraw w => await MoneyAsync(await wallet.WithdrawAsync(player, w.Amount), "withdrawal", w.Amount),
        Bet b => await BetAsync(b.Amount),
        Balance => await BalanceAsync(),
        Help => HelpText,
        Exit => "Thank you for playing! Hope to see you again soon.",
        Invalid i => i.Message,
        _ => "Unknown command."
    };

    private async Task<string> BalanceAsync()
    {
        var r = await wallet.GetBalanceAsync(player);
        return r.Status == ApiStatus.Ok ? $"Your current balance is: {Fmt(r.Value)}" : Failure(r);
    }

    private async Task<string> MoneyAsync(ApiResult<decimal> r, string noun, decimal amount)
    {
        if (r.Status == ApiStatus.Ok)
            return $"Your {noun} of {Fmt(amount)} was successful. Your current balance is: {Fmt(r.Value)}";
        if (r.Status == ApiStatus.BusinessError && r.ErrorCode == "insufficient_funds")
            return await InsufficientFundsAsync();
        return Failure(r);
    }

    private async Task<string> InsufficientFundsAsync() =>
        $"Insufficient funds. Your current balance is: {await WalletBalanceTextAsync()}";

    private async Task<string> BetAsync(decimal amount)
    {
        // The round id is the idempotency key: every client retry of this bet reuses it.
        var roundId = Guid.NewGuid();
        ApiResult<RoundView> result = null!;
        for (var attempt = 1; attempt <= BetAttempts; attempt++)
        {
            result = await game.PlaceBetAsync(player, roundId, amount);
            if (result.Status != ApiStatus.Unavailable) break;
            if (attempt < BetAttempts) await delay(TimeSpan.FromMilliseconds(200 * attempt));
        }

        switch (result.Status)
        {
            case ApiStatus.Ok:
                return await SettledAsync(result.Value!);

            case ApiStatus.Pending:
                return await PendingAsync(player, roundId, result.Value);

            case ApiStatus.BusinessError:
                return result.ErrorCode switch
                {
                    "insufficient_funds" => await InsufficientFundsAsync(),
                    "validation_error" => "Bets must be between $1 and $10.",
                    "round_cancelled" => await CancelledAsync(result.Value),
                    "round_rejected" => await RejectedAsync(result.Value),
                    "round_needs_attention" => await NeedsAttentionAsync(result.Value),
                    _ => Failure(result)
                };

            default:
                return UnavailableMessage;
        }
    }

    private async Task<string> PendingAsync(string playerName, Guid roundId, RoundView? initial)
    {
        var lines = new List<string>
        {
            initial?.Win is > 0 and var win
                ? $"You won {Fmt(win)} - it will be credited shortly."
                : "Your bet is being processed; any charge will be refunded if it can't complete."
        };

        for (var i = 0; i < pollAttempts; i++)
        {
            await delay(_pollInterval);
            var r = await game.GetRoundAsync(playerName, roundId);
            if (r.Status != ApiStatus.Ok || r.Value is null) continue;

            switch (r.Value.Status)
            {
                case "Settled":
                    lines.Add(await SettledAsync(r.Value));
                    return string.Join("\n", lines);
                case "Cancelled":
                    lines.Add(await CancelledAsync(r.Value));
                    return string.Join("\n", lines);
                case "Rejected":
                    lines.Add(await RejectedAsync(r.Value));
                    return string.Join("\n", lines);
                case "NeedsAttention":
                    lines.Add(await NeedsAttentionAsync(r.Value));
                    return string.Join("\n", lines);
            }
        }

        lines.Add($"Your round will be settled automatically. Your current balance is: {await WalletBalanceTextAsync()}");
        return string.Join("\n", lines);
    }

    private async Task<string> SettledAsync(RoundView round)
    {
        var balance = round.Balance is { } b ? Fmt(b) : await WalletBalanceTextAsync();
        return round.Win is > 0 and var win
            ? $"Congrats - you won {Fmt(win)}! Your current balance is: {balance}"
            : $"No luck this time! Your current balance is: {balance}";
    }

    private async Task<string> CancelledAsync(RoundView? round)
    {
        var balance = round?.Balance is { } b ? Fmt(b) : await WalletBalanceTextAsync();
        return $"Your bet could not be completed and was refunded. Your current balance is: {balance}";
    }

    private async Task<string> RejectedAsync(RoundView? round)
    {
        var balance = round?.Balance is { } b ? Fmt(b) : await WalletBalanceTextAsync();
        return $"Sorry, that could not be completed. Your current balance is: {balance}";
    }

    private async Task<string> NeedsAttentionAsync(RoundView? round)
    {
        var balance = round?.Balance is { } b ? Fmt(b) : await WalletBalanceTextAsync();
        return $"Your round has been flagged for review. Your current balance is: {balance}";
    }

    private async Task<string> WalletBalanceTextAsync()
    {
        var r = await wallet.GetBalanceAsync(player);
        return r.Status == ApiStatus.Ok ? Fmt(r.Value) : "unavailable";
    }

    private static string Failure<T>(ApiResult<T> r) =>
        r.Status == ApiStatus.BusinessError
            ? $"Sorry, that could not be completed{(string.IsNullOrWhiteSpace(r.Detail) ? "." : ": " + r.Detail)}"
            : UnavailableMessage;

    private static string Fmt(decimal d) => MoneyFormatter.Format(d);
}
