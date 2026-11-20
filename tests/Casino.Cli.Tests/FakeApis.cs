using Casino.Cli;
using Casino.Cli.Api;

namespace Casino.Cli.Tests;

public class FakeWallet : IWalletApi
{
    public ApiResult<decimal> DepositResult { get; set; } = new(ApiStatus.Ok, 0m, null, null);
    public ApiResult<decimal> WithdrawResult { get; set; } = new(ApiStatus.Ok, 0m, null, null);
    public ApiResult<decimal> BalanceResult { get; set; } = new(ApiStatus.Ok, 0m, null, null);
    public Task<ApiResult<decimal>> DepositAsync(string player, decimal amount) => Task.FromResult(DepositResult);
    public Task<ApiResult<decimal>> WithdrawAsync(string player, decimal amount) => Task.FromResult(WithdrawResult);
    public Task<ApiResult<decimal>> GetBalanceAsync(string player) => Task.FromResult(BalanceResult);
}

public class FakeGame : IGameApi
{
    public ApiResult<RoundView> BetResult { get; set; } = null!;
    public Queue<ApiResult<RoundView>> GetResults { get; } = new();
    public ApiResult<RoundView> GetDefault { get; set; } = new(ApiStatus.Unavailable, null, null, null);
    public int GetCalls { get; private set; }
    public List<Guid> BetIds { get; } = new();

    public Task<ApiResult<RoundView>> PlaceBetAsync(string player, Guid roundId, decimal amount)
    {
        BetIds.Add(roundId);
        return Task.FromResult(BetResult);
    }

    public Task<ApiResult<RoundView>> GetRoundAsync(string player, Guid roundId)
    {
        GetCalls++;
        return Task.FromResult(GetResults.Count > 0 ? GetResults.Dequeue() : GetDefault);
    }
}
