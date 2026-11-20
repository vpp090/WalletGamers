using Casino.Cli;
using Casino.Cli.Api;

if (!CliOptions.TryParse(args, Environment.GetEnvironmentVariable, out var options, out var error))
{
    Console.Error.WriteLine(error);
    return 2;
}

using var walletHttp = new HttpClient { BaseAddress = options!.WalletUrl, Timeout = TimeSpan.FromSeconds(10) };
using var gameHttp = new HttpClient { BaseAddress = options.GameUrl, Timeout = TimeSpan.FromSeconds(30) };

var session = new CasinoSession(options.Player, new WalletApi(walletHttp), new GameApi(gameHttp), t => Task.Delay(t));

Console.WriteLine($"Welcome, {options.Player}! {CasinoSession.HelpText}");
while (true)
{
    Console.Write("> ");
    var line = Console.ReadLine();
    if (line is null) break;

    var command = CommandParser.Parse(line);
    try
    {
        Console.WriteLine(await session.ExecuteAsync(command));
    }
    catch (Exception)
    {
        Console.WriteLine(CasinoSession.UnavailableMessage);
    }

    if (command is Exit) break;
}

return 0;
