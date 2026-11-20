using System.Globalization;

namespace Casino.Cli;

public static class MoneyFormatter
{
    public static string Format(decimal amount) =>
        amount == decimal.Truncate(amount)
            ? "$" + amount.ToString("0", CultureInfo.InvariantCulture)
            : "$" + amount.ToString("0.00", CultureInfo.InvariantCulture);
}
