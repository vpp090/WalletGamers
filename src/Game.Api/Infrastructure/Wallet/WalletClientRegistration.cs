using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Api.Infrastructure.Wallet;

public static class WalletClientRegistration
{
    public static IServiceCollection AddWalletClient(this IServiceCollection services, IConfiguration configuration)
    {
        var baseUrl = configuration["Wallet:BaseUrl"]
            ?? throw new InvalidOperationException("Configuration value 'Wallet:BaseUrl' is required.");
        var resilience = configuration.GetSection("Wallet:Resilience");
        var attempts = Math.Max(1, resilience.GetValue("MaxRetryAttempts", 3));
        var baseDelay = resilience.GetValue("BaseDelay", TimeSpan.FromMilliseconds(200));

        services.AddHttpClient<IWalletClient, WalletClient>(c => c.BaseAddress = new Uri(baseUrl))
            .AddStandardResilienceHandler(o =>
            {
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
                o.Retry.MaxRetryAttempts = attempts;
                o.Retry.Delay = baseDelay;
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            });
        return services;
    }
}
