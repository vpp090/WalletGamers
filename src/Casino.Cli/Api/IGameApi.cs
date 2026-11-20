namespace Casino.Cli.Api;

public interface IGameApi
{
    Task<ApiResult<RoundView>> PlaceBetAsync(string player, Guid roundId, decimal amount);
    Task<ApiResult<RoundView>> GetRoundAsync(string player, Guid roundId);
}
