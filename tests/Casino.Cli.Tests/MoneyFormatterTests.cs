using Casino.Cli;
using FluentAssertions;

namespace Casino.Cli.Tests;

public class MoneyFormatterTests
{
    [Theory]
    [InlineData(10, "$10")]
    [InlineData(8.5, "$8.50")]
    [InlineData(0, "$0")]
    [InlineData(13.50, "$13.50")]
    public void Formats(double v, string expected) => MoneyFormatter.Format((decimal)v).Should().Be(expected);
}
