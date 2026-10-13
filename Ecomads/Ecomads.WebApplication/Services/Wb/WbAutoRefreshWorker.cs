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
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public async Task RunOnceAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Wb:AutoRefresh:Enabled")) return;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
        var scheduler = scope.ServiceProvider.GetRequiredService<WbScheduledRefreshService>();
        var storeIds = await db.Stores.AsNoTracking().Where(x => x.ApiKey != null && x.AutoRefreshEnabled &&
            !x.AutoRefreshBlocked && (x.TokenExpiresAtUtc == null || x.TokenExpiresAtUtc > nowUtc))
            .Select(x => x.Id).ToListAsync(cancellationToken);
        var rolloutStores = (configuration["Wb:AutoRefresh:StoreIds"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => Guid.TryParse(x.Trim(), out var id) ? id : Guid.Empty).ToHashSet();
        if (rolloutStores.Count > 0) storeIds = storeIds.Where(rolloutStores.Contains).ToList();
        foreach (var storeId in storeIds)
        {
            try { await scheduler.RunAsync(storeId, nowUtc, cancellationToken); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogError(error, "WB automatic refresh failed for store {StoreId}", storeId);
            }
        }
    }
}
