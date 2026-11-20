using Microsoft.AspNetCore.Mvc;
using Wallet.Api.Domain;

namespace Wallet.Api.Controllers;

public static class ProblemMapping
{
    public static ObjectResult ToProblem(WalletError error)
    {
        var (status, code, title) = error switch
        {
            WalletError.InsufficientFunds => (422, "insufficient_funds", "Insufficient funds"),
            WalletError.IdempotencyConflict => (409, "idempotency_conflict", "Transaction id already used with different details"),
            WalletError.TransactionCancelled => (409, "transaction_cancelled", "Transaction has been cancelled"),
            WalletError.NotCancellable => (409, "not_cancellable", "Transaction cannot be cancelled"),
            WalletError.RoundCancelled => (409, "round_cancelled", "Round has been cancelled"),
            _ => throw new ArgumentOutOfRangeException(nameof(error), error, null),
        };
        return Problem(status, code, title);
    }

    public static ObjectResult Problem(int status, string code, string title, string? detail = null)
    {
        var pd = new ProblemDetails { Status = status, Title = title, Detail = detail };
        pd.Extensions["code"] = code;
        return new ObjectResult(pd) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }

    public static ObjectResult Validation(string detail) => Problem(400, "validation_error", "Validation error", detail);
}
