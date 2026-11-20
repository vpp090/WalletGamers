namespace Wallet.Api.Domain;

public sealed record WalletResult(string TransactionId, long BalanceCents, WalletError? Error)
{
    public bool IsSuccess => Error is null;
}
