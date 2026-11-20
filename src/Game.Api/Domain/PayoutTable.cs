namespace Game.Api.Domain;

public class PayoutTable(IRandomSource random)
{
    public Payout Roll(Money bet)
    {
        var u = random.NextDouble();
        if (u < 0.5)
            return new Payout(Outcome.Lose, Money.Zero);

        var v = (decimal)random.NextDouble();
        var oneMinusV = 1m - v;
        long cents;
        if (u < 0.9)
        {
            cents = (long)Math.Floor(bet.Cents * 2m * oneMinusV);
            if (cents < 1) cents = 1;
        }
        else
        {
            cents = (long)Math.Floor(bet.Cents * (2m + 8m * oneMinusV));
            if (cents <= 2 * bet.Cents) cents = 2 * bet.Cents + 1;
        }

        return new Payout(Outcome.Win, new Money(cents));
    }
}
