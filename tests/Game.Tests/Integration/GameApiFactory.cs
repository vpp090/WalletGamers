using Game.Api.Domain;
using Game.Api.Infrastructure.Wallet;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Game.Tests.Integration;

public sealed class GameApiFactory(string connectionString, IWalletClient wallet, IRandomSource random, TimeProvider? clock = null)
    : WebApplicationFactory<Game.Api.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Game", connectionString);
        builder.UseSetting("Recovery:Enabled", "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWalletClient>();
            services.AddSingleton(wallet);
            services.RemoveAll<IRandomSource>();
            services.AddSingleton(random);
            if (clock is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(clock);
            }
        });
    }
}
