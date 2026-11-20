using Game.Api.Domain;

namespace Game.Api.Infrastructure.Wallet;

public interface IWalletClient
{
    Task<WalletCallResult> DebitAsync(string playerId, string transactionId, Guid roundId, Money amount, CancellationToken ct);

    Task<WalletCallResult> CreditAsync(string playerId, string transactionId, Guid roundId, Money amount, CancellationToken ct);

    Task<WalletCallResult> CancelAsync(string playerId, string transactionId, string originalTransactionId, CancellationToken ct);
}
