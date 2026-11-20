using Microsoft.AspNetCore.Mvc;
using Wallet.Api.Application;
using Wallet.Api.Domain;

namespace Wallet.Api.Controllers;

[ApiController]
[Route("api/players/{playerId}")]
public class AccountsController(WalletService wallet) : ControllerBase
{
    [HttpGet("balance")]
    public async Task<ActionResult<BalanceResponse>> GetBalance(string playerId, CancellationToken ct) =>
        new BalanceResponse((await wallet.GetBalanceAsync(playerId, ct)).ToDecimal());

    [HttpPost("deposits")]
    public async Task<IActionResult> Deposit(string playerId, MoneyRequest request, CancellationToken ct)
    {
        if (!TryParse(request, out var amount, out var invalid)) return invalid!;
        return ToResponse(await wallet.DepositAsync(playerId, request.TransactionId, amount, ct));
    }

    [HttpPost("withdrawals")]
    public async Task<IActionResult> Withdraw(string playerId, MoneyRequest request, CancellationToken ct)
    {
        if (!TryParse(request, out var amount, out var invalid)) return invalid!;
        return ToResponse(await wallet.WithdrawAsync(playerId, request.TransactionId, amount, ct));
    }

    [HttpPost("bets")]
    public async Task<IActionResult> Bet(string playerId, RoundMoneyRequest request, CancellationToken ct)
    {
        if (!TryParse(request.Amount, out var amount, out var invalid)) return invalid!;
        return ToResponse(await wallet.BetAsync(playerId, request.TransactionId, request.RoundId, amount, ct));
    }

    [HttpPost("wins")]
    public async Task<IActionResult> Win(string playerId, RoundMoneyRequest request, CancellationToken ct)
    {
        if (!TryParse(request.Amount, out var amount, out var invalid)) return invalid!;
        return ToResponse(await wallet.WinAsync(playerId, request.TransactionId, request.RoundId, amount, ct));
    }

    [HttpPost("cancellations")]
    public async Task<IActionResult> Cancel(string playerId, CancelRequest request, CancellationToken ct) =>
        ToResponse(await wallet.CancelAsync(playerId, request.TransactionId, request.OriginalTransactionId, ct));

    private const long MaxAmountCents = 100_000_000;

    private static bool TryParse(MoneyRequest request, out Money amount, out IActionResult? invalid) =>
        TryParse(request.Amount, out amount, out invalid);

    private static bool TryParse(decimal value, out Money amount, out IActionResult? invalid)
    {
        invalid = null;
        amount = default;
        if (value > MaxAmountCents / 100m)
            invalid = ProblemMapping.Validation("Amount must not exceed $1,000,000.00 per transaction.");
        else if (!Money.TryFromDecimal(value, out amount))
            invalid = ProblemMapping.Validation("Amount must have at most 2 decimal places.");
        else if (!amount.IsPositive)
            invalid = ProblemMapping.Validation("Amount must be greater than zero.");
        return invalid is null;
    }

    private IActionResult ToResponse(WalletResult result) =>
        result.IsSuccess
            ? Ok(new TransactionResponse(result.TransactionId, new Money(result.BalanceCents).ToDecimal()))
            : ProblemMapping.ToProblem(result.Error!.Value);
}
