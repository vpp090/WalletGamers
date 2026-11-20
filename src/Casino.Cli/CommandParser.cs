using System.Globalization;

namespace Casino.Cli;

public static class CommandParser
{
    public static Command Parse(string? line)
    {
        var parts = (line ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return new Invalid("Please enter a command. Type 'help' to see what you can do.");

        var verb = parts[0].ToLowerInvariant();
        switch (verb)
        {
            case "balance" or "exit" or "help":
                if (parts.Length != 1)
                    return new Invalid($"'{verb}' does not take any arguments.");
                return verb switch { "balance" => new Balance(), "exit" => new Exit(), _ => new Help() };
            case "deposit" or "withdraw" or "bet":
                if (parts.Length != 2)
                    return new Invalid($"Usage: {verb} <amount>");
                if (!TryParseAmount(parts[1], out var amount))
                    return new Invalid("Please enter a positive amount with at most 2 decimal places, e.g. 10 or 8.50.");
                return verb switch { "deposit" => new Deposit(amount), "withdraw" => new Withdraw(amount), _ => new Bet(amount) };
            default:
                return new Invalid($"Unknown command '{parts[0]}'. Type 'help' to see what you can do.");
        }
    }

    private static bool TryParseAmount(string text, out decimal amount) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount)
        && amount > 0
        && decimal.Round(amount, 2) == amount;
}
