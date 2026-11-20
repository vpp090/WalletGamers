namespace Game.Api.Domain;

public static class BetRules
{
    public const decimal Min = 1.00m;
    public const decimal Max = 10.00m;

    public static bool TryCreate(decimal amount, out Money bet, out string? error)
    {
        if (amount < Min || amount > Max)
        {
            bet = default;
            error = $"Bet must be between {Min:0.00} and {Max:0.00}.";
            return false;
        }

        if (!Money.TryFromDecimal(amount, out bet))
        {
            error = "Bet must have at most 2 decimal places.";
            return false;
        }

        error = null;
        return true;
    }
}
