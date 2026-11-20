using Microsoft.EntityFrameworkCore;
using Wallet.Api.Application;
using Wallet.Api.Controllers;
using Wallet.Api.Infrastructure;

namespace Wallet.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddDbContext<WalletDbContext>(o =>
            o.UseNpgsql(builder.Configuration.GetConnectionString("Wallet")));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<WalletService>();
        builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
            ctx.ProblemDetails.Extensions.TryAdd("code", ctx.ProblemDetails.Status == 500 ? "internal_error" : "http_" + ctx.ProblemDetails.Status));
        builder.Services.AddHealthChecks().AddDbContextCheck<WalletDbContext>();
        builder.Services.AddControllers().ConfigureApiBehaviorOptions(o =>
            o.InvalidModelStateResponseFactory = ctx =>
            {
                var errors = ctx.ModelState.Where(e => e.Value?.Errors.Count > 0)
                    .Select(e => $"{e.Key}: {string.Join(' ', e.Value!.Errors.Select(x => x.ErrorMessage))}".Trim());
                return ProblemMapping.Validation(string.Join("; ", errors));
            });

        var app = builder.Build();

        await using (var scope = app.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<WalletDbContext>().Database.MigrateAsync();

        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.MapControllers();
        app.MapHealthChecks("/health");
        await app.RunAsync();
    }
}
