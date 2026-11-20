namespace Casino.Cli.Api;

public interface IWalletApi
{
    Task<ApiResult<decimal>> DepositAsync(string player, decimal amount);
    Task<ApiResult<decimal>> WithdrawAsync(string player, decimal amount);
    Task<ApiResult<decimal>> GetBalanceAsync(string player);
}
