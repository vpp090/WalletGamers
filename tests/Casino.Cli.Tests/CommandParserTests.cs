using Casino.Cli;
using FluentAssertions;

namespace Casino.Cli.Tests;

public class CommandParserTests
{
    [Fact] public void Deposit_parses() => CommandParser.Parse("deposit 10").Should().Be(new Deposit(10m));
    [Fact] public void Bet_is_case_insensitive() => CommandParser.Parse("BET 5").Should().Be(new Bet(5m));
    [Fact] public void Withdraw_decimal() => CommandParser.Parse("withdraw 13.50").Should().Be(new Withdraw(13.50m));
    [Fact] public void Exit_parses() => CommandParser.Parse("exit").Should().Be(new Exit());

    [Theory]
    [InlineData("bet abc")]
    [InlineData("deposit -5")]
    [InlineData("deposit 0")]
    [InlineData("deposit 10.005")]
    [InlineData("withdraw")]
    [InlineData("deposit 1e3")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("dance 5")]
    public void Invalid_input(string? line)
    {
        var c = CommandParser.Parse(line);
        c.Should().BeOfType<Invalid>().Which.Message.Should().NotBeNullOrWhiteSpace();
    }
}
