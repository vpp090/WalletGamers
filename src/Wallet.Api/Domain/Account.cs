namespace Wallet.Api.Domain;

public class Account
{
    public required string PlayerId { get; set; }
    public long BalanceCents { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
