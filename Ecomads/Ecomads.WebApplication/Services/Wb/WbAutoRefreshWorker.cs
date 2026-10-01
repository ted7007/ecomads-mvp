using Ecomads.WebApplication.Data;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Wb;

public sealed class WbAutoRefreshWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<WbAutoRefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("Wb:AutoRefresh:Enabled")) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "WB automatic refresh pass failed");
            }
            try { await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public async Task RunOnceAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Wb:AutoRefresh:Enabled")) return;
        var moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, moscow);
        if (localNow.TimeOfDay < TimeSpan.FromHours(6)) return;
        var localStart = DateTime.SpecifyKind(localNow.Date.AddHours(6), DateTimeKind.Unspecified);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, moscow);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
        var planner = scope.ServiceProvider.GetRequiredService<WbSyncPlanner>();
        var stores = await db.Stores.Where(x => x.ApiKey != null && x.TokenExpiresAtUtc > nowUtc)
            .ToListAsync(cancellationToken);
        foreach (var store in stores)
        {
            if (await db.WbSyncJobs.AnyAsync(x => x.StoreId == store.Id && x.RunId != null &&
                x.CreatedAtUtc >= startUtc, cancellationToken)) continue;
            try { await planner.StartRefreshAsync(store, cancellationToken); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogError(error, "WB automatic refresh failed for store {StoreId}", store.Id);
            }
        }
    }
}
