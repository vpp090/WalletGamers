namespace Casino.Cli;

public abstract record Command;
public sealed record Deposit(decimal Amount) : Command;
public sealed record Withdraw(decimal Amount) : Command;
public sealed record Bet(decimal Amount) : Command;
public sealed record Balance : Command;
public sealed record Exit : Command;
public sealed record Help : Command;
public sealed record Invalid(string Message) : Command;
