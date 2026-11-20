using System.ComponentModel.DataAnnotations;

namespace Wallet.Api.Controllers;

public record BalanceResponse(decimal Balance);

public record MoneyRequest([Required] string TransactionId, decimal Amount);

public record RoundMoneyRequest([Required] string TransactionId, Guid RoundId, decimal Amount);

public record CancelRequest([Required] string TransactionId, [Required] string OriginalTransactionId);

public record TransactionResponse(string TransactionId, decimal Balance);
