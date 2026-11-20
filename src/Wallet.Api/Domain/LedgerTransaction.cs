namespace Wallet.Api.Domain;

public class LedgerTransaction
{
    public required string Id { get; set; }
    public required string PlayerId { get; set; }
    public TransactionType Type { get; set; }
    public long AmountCents { get; set; }
    public long BalanceAfterCents { get; set; }
    public Guid? RoundId { get; set; }
    public string? ReferenceTransactionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
