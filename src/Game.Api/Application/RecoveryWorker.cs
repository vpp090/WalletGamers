using Microsoft.Extensions.Options;

namespace Game.Api.Application;

public class RecoveryWorker(IServiceScopeFactory scopes, IOptions<RecoveryOptions> options, ILogger<RecoveryWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.Interval);
        try
        {
            do
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var resolved = await scope.ServiceProvider.GetRequiredService<RecoveryService>().RunOnceAsync(stoppingToken);
                    if (resolved > 0)
                        logger.LogInformation("Recovery resolved {Count} round(s)", resolved);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Recovery pass failed");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
        }
    }
}
