namespace Game.Api.Infrastructure.Wallet;

internal record WalletRoundMoneyRequest(string TransactionId, Guid RoundId, decimal Amount);

internal record WalletCancelRequest(string TransactionId, string OriginalTransactionId);

internal record WalletTransactionResponse(string TransactionId, decimal Balance);
