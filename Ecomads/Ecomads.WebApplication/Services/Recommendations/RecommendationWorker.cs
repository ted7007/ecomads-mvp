using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ecomads.WebApplication.Services.Recommendations;

/// <summary>The same fixed Moscow window and engine serve the API, summary and background.</summary>
public sealed class RecommendationWorker(IServiceScopeFactory scopes, IOptions<RecommendationOptions> options,
    ILogger<RecommendationWorker> logger) : BackgroundService
{
    private DateOnly? cleanedOn;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(2));
        do
        {
            try { await RunOnceAsync(DateTime.UtcNow, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Recommendation background pass failed"); }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    public async Task RunOnceAsync(DateTime nowUtc, CancellationToken ct)
    {
        if (!options.Value.Enabled) return;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
        var stores = await db.Stores.AsNoTracking().Where(x => x.ApiKey != null).ToListAsync(ct);
        var today = WbSyncPlanner.Today(nowUtc);
        if (cleanedOn != today)
        {
            var cutoff = nowUtc.AddDays(-options.Value.RetentionDays);
            await db.WbRecommendationRuns.Where(x => x.CalculatedAtUtc < cutoff &&
                !db.WbResultExposures.Any(e => e.RunId == x.Id) &&
                !db.WbSnapshotDecisions.Any(d => d.RunId == x.Id) &&
                !(x.SchemaVersion < 3 && db.WbRecommendationDecisions.Any(d => d.SellerId == x.SellerId))).ExecuteDeleteAsync(ct);
            await db.WbArticleObservations.Where(x => x.ObservedAtUtc < cutoff).ExecuteDeleteAsync(ct);
            cleanedOn = today;
        }
        var window = RecommendationPeriods.Economic(nowUtc);
        foreach (var store in stores)
        {
            try
            {
                var campaigns = await db.Campaigns.AsNoTracking().Where(x => x.StoreId == store.Id &&
                    (x.WbStatus == 7 || x.WbStatus == 9 || x.WbStatus == 11)).ToListAsync(ct);
                if (store.AutoRefreshEnabled && !store.AutoRefreshBlocked && (store.TokenExpiresAtUtc == null || store.TokenExpiresAtUtc > nowUtc))
                {
                    try { await scope.ServiceProvider.GetRequiredService<OperationalSync>().RefreshAsync(store, nowUtc, ct); }
                    catch (Exception error) when (error is not OperationCanceledException) { logger.LogWarning(error, "Operational import failed for {StoreId}", store.Id); }
                    await scope.ServiceProvider.GetRequiredService<AdvertisingSettingsSync>().RefreshAsync(store, nowUtc, ct);
                    var windows = new[] { window, new RecommendationWindow(window.StartDate.AddDays(-14), window.StartDate.AddDays(-1)) };
                    var planner = scope.ServiceProvider.GetRequiredService<WbSyncPlanner>();
                    var queued = false;
                    foreach (var target in windows)
                    {
                        for (var start = target.StartDate; start <= target.EndDate; start = start.AddDays(7))
                        {
                            var end = start.AddDays(6) < target.EndDate ? start.AddDays(6) : target.EndDate;
                            var result = await planner.EnqueueMissingClustersAsync(store, start, end, ct);
                            if (result.Job == null) continue;
                            queued = true;
                            break;
                        }
                        if (queued) break;
                    }
                }
                var reports = new List<RecommendationResponse>();
                foreach (var campaign in campaigns)
                {
                    var report = await scope.ServiceProvider.GetRequiredService<RecommendationService>().CalculateAsync(store.SellerId, campaign.Id, window, ct);
                    if (report != null) reports.Add(report);
                }
                if (options.Value.ResultsEnabled)
                    await scope.ServiceProvider.GetRequiredService<ResultsService>().RunStoreAsync(store.Id, ct);
                foreach (var report in reports)
                    await scope.ServiceProvider.GetRequiredService<ResultsService>().ObserveConditionsAsync(store.Id, report, ct);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogError(error, "Recommendation calculation failed for store {StoreId}", store.Id);
            }
        }
    }
}
