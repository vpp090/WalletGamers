using Game.Api.Domain;
using Game.Api.Infrastructure.Wallet;

namespace Game.Tests.Integration;

public sealed record WalletCall(string PlayerId, string TransactionId, Guid? RoundId, long? AmountCents, string? Original);

public sealed class FakeWalletClient : IWalletClient
{
    private readonly object _gate = new();

    public List<WalletCall> Debits { get; } = [];
    public List<WalletCall> Credits { get; } = [];
    public List<WalletCall> Cancels { get; } = [];

    public WalletCallResult DebitResult { get; set; } = WalletCallResult.Success(0);
    public WalletCallResult CreditResult { get; set; } = WalletCallResult.Success(0);
    public WalletCallResult CancelResult { get; set; } = WalletCallResult.Success(0);

    public HashSet<string> ThrowForPlayers { get; } = [];

    public int TotalCalls
    {
        get { lock (_gate) return Debits.Count + Credits.Count + Cancels.Count; }
    }

    public Task<WalletCallResult> DebitAsync(string playerId, string transactionId, Guid roundId, Money amount, CancellationToken ct)
    {
        lock (_gate) Debits.Add(new(playerId, transactionId, roundId, amount.Cents, null));
        return Task.FromResult(DebitResult);
    }

    public Task<WalletCallResult> CreditAsync(string playerId, string transactionId, Guid roundId, Money amount, CancellationToken ct)
    {
        lock (_gate) Credits.Add(new(playerId, transactionId, roundId, amount.Cents, null));
        ThrowIfConfigured(playerId);
        return Task.FromResult(CreditResult);
    }

    public Task<WalletCallResult> CancelAsync(string playerId, string transactionId, string originalTransactionId, CancellationToken ct)
    {
        lock (_gate) Cancels.Add(new(playerId, transactionId, null, null, originalTransactionId));
        ThrowIfConfigured(playerId);
        return Task.FromResult(CancelResult);
    }

    private void ThrowIfConfigured(string playerId)
    {
        lock (_gate)
        {
            if (ThrowForPlayers.Contains(playerId))
                throw new InvalidOperationException("Simulated wallet failure.");
        }
    }
}
