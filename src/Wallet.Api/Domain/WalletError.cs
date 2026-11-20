namespace Wallet.Api.Domain;

public enum WalletError { InsufficientFunds, IdempotencyConflict, TransactionCancelled, NotCancellable, RoundCancelled }
