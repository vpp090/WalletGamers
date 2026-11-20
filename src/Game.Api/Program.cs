using Game.Api.Application;
using Game.Api.Controllers;
using Game.Api.Domain;
using Game.Api.Infrastructure;
using Game.Api.Infrastructure.Wallet;
using Microsoft.EntityFrameworkCore;

namespace Game.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddDbContext<GameDbContext>(o =>
            o.UseNpgsql(builder.Configuration.GetConnectionString("Game")));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IRandomSource, CryptoRandomSource>();
        builder.Services.AddSingleton<PayoutTable>();
        builder.Services.AddWalletClient(builder.Configuration);
        builder.Services.AddScoped<RoundService>();
        builder.Services.Configure<RecoveryOptions>(builder.Configuration.GetSection(RecoveryOptions.SectionName));
        builder.Services.AddScoped<RecoveryService>();
        if (builder.Configuration.GetValue(RecoveryOptions.SectionName + ":Enabled", true))
            builder.Services.AddHostedService<RecoveryWorker>();
        builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
            ctx.ProblemDetails.Extensions.TryAdd("code", ctx.ProblemDetails.Status == 500 ? "internal_error" : "http_" + ctx.ProblemDetails.Status));
        builder.Services.AddHealthChecks().AddDbContextCheck<GameDbContext>();
        builder.Services.AddControllers().ConfigureApiBehaviorOptions(o =>
            o.InvalidModelStateResponseFactory = ctx =>
            {
                var errors = ctx.ModelState.Where(e => e.Value?.Errors.Count > 0)
                    .Select(e => $"{e.Key}: {string.Join(' ', e.Value!.Errors.Select(x => x.ErrorMessage))}".Trim());
                return ProblemMapping.Validation(string.Join("; ", errors));
            });

        var app = builder.Build();

        await using (var scope = app.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<GameDbContext>().Database.MigrateAsync();

        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.MapControllers();
        app.MapHealthChecks("/health");
        await app.RunAsync();
    }
}
