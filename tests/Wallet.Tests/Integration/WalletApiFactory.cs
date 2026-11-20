using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Wallet.Tests.Integration;

public sealed class WalletApiFactory(string connectionString) : WebApplicationFactory<Wallet.Api.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Wallet", connectionString);
    }
}
