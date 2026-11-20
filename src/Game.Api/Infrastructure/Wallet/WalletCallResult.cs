namespace Game.Api.Infrastructure.Wallet;

public enum WalletCallStatus { Success, Rejected, Unavailable }

public record WalletCallResult(WalletCallStatus Status, long? BalanceCents, string? ErrorCode)
{
    public static WalletCallResult Success(long balanceCents) => new(WalletCallStatus.Success, balanceCents, null);
    public static WalletCallResult Rejected(string? code) => new(WalletCallStatus.Rejected, null, code);
    public static WalletCallResult Unavailable() => new(WalletCallStatus.Unavailable, null, null);
}
