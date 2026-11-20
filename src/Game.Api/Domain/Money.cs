namespace Game.Api.Domain;

public readonly record struct Money(long Cents)
{
    public static Money Zero => new(0);

    public bool IsPositive => Cents > 0;

    public static bool TryFromDecimal(decimal amount, out Money money)
    {
        if (Math.Abs(amount) > long.MaxValue / 100m)
        {
            money = default;
            return false;
        }

        var cents = amount * 100m;
        if (decimal.Truncate(cents) != cents)
        {
            money = default;
            return false;
        }

        money = new Money((long)cents);
        return true;
    }

    public decimal ToDecimal() => Cents / 100m;

    public static Money operator +(Money a, Money b) => new(checked(a.Cents + b.Cents));
    public static Money operator -(Money a, Money b) => new(checked(a.Cents - b.Cents));
    public static bool operator <(Money a, Money b) => a.Cents < b.Cents;
    public static bool operator >(Money a, Money b) => a.Cents > b.Cents;
    public static bool operator <=(Money a, Money b) => a.Cents <= b.Cents;
    public static bool operator >=(Money a, Money b) => a.Cents >= b.Cents;
}
