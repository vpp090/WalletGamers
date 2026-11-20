namespace Game.Api.Domain;

public enum Outcome { Lose, Win }

public record Payout(Outcome Outcome, Money Win);
