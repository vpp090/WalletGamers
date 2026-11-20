namespace Casino.Cli;

public record CliOptions(string Player, Uri WalletUrl, Uri GameUrl)
{
    public const string DefaultWalletUrl = "http://localhost:5001";
    public const string DefaultGameUrl = "http://localhost:5002";

    public static bool TryParse(string[] args, Func<string, string?> env, out CliOptions? options, out string? error)
    {
        options = null;
        string? player = null;
        var wallet = env("WALLET_URL");
        var game = env("GAME_URL");

        for (var i = 0; i < args.Length; i++)
        {
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
            {
                error = $"Missing value for {args[i]}.";
                return false;
            }

            switch (args[i])
            {
                case "--player": player = args[++i]; break;
                case "--wallet-url": wallet = args[++i]; break;
                case "--game-url": game = args[++i]; break;
                default:
                    error = $"Unknown option {args[i]}.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(player))
        {
            error = "Usage: Casino.Cli --player <name> [--wallet-url <url>] [--game-url <url>]";
            return false;
        }

        if (!Uri.TryCreate(string.IsNullOrWhiteSpace(wallet) ? DefaultWalletUrl : wallet, UriKind.Absolute, out var walletUri)
            || !Uri.TryCreate(string.IsNullOrWhiteSpace(game) ? DefaultGameUrl : game, UriKind.Absolute, out var gameUri))
        {
            error = "Wallet and game URLs must be absolute URLs.";
            return false;
        }

        options = new CliOptions(player.Trim(), walletUri, gameUri);
        error = null;
        return true;
    }
}
