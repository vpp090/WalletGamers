using Game.Api.Application;
using Game.Api.Domain;
using Game.Api.Infrastructure.Wallet;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace System.Tests;

public sealed class SystemFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Wallet.Api.Program> _walletFactory = null!;
    private WebApplicationFactory<Game.Api.Program> _gameFactory = null!;

    public FaultInjectingHandler Fault { get; } = new();
    public ScriptedRandomSource Random { get; } = new();
    public OffsetTimeProvider Clock { get; } = new();

    public HttpClient Wallet { get; private set; } = null!;
    public HttpClient Game { get; private set; } = null!;
    public IServiceProvider WalletServices => _walletFactory.Services;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var baseCs = _container.GetConnectionString();
        await using (var conn = new NpgsqlConnection(baseCs))
        {
            await conn.OpenAsync();
            foreach (var db in new[] { "wallet", "game" })
            {
                await using var cmd = new NpgsqlCommand($"CREATE DATABASE {db}", conn);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        string Cs(string db) => new NpgsqlConnectionStringBuilder(baseCs) { Database = db }.ConnectionString;

        _walletFactory = new WebApplicationFactory<Wallet.Api.Program>().WithWebHostBuilder(b =>
            b.UseSetting("ConnectionStrings:Wallet", Cs("wallet")));
        Wallet = _walletFactory.CreateClient();

        _gameFactory = new WebApplicationFactory<Game.Api.Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Game", Cs("game"));
            b.UseSetting("Wallet:BaseUrl", "http://wallet.test/");
            b.UseSetting("Wallet:Resilience:MaxRetryAttempts", "1");
            b.UseSetting("Wallet:Resilience:BaseDelay", "00:00:00.010");
            b.UseSetting("Recovery:Enabled", "false");
            b.ConfigureTestServices(services =>
            {
                Fault.InnerHandler = _walletFactory.Server.CreateHandler();
                services.AddHttpClient<IWalletClient, WalletClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => Fault);
                services.RemoveAll<IRandomSource>();
                services.AddSingleton<IRandomSource>(Random);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
            });
        });
        Game = _gameFactory.CreateClient();
    }

    public async Task<int> RunRecoveryAsync()
    {
        await using var scope = _gameFactory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<RecoveryService>().RunOnceAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        Wallet?.Dispose();
        Game?.Dispose();
        if (_gameFactory is not null) await _gameFactory.DisposeAsync();
        if (_walletFactory is not null) await _walletFactory.DisposeAsync();
        await _container.DisposeAsync();
    }
}
