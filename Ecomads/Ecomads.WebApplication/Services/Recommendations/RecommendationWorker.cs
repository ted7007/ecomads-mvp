using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ecomads.WebApplication.Services.Recommendations;

/// <summary>The same engine serves interactive periods and an independent standard background window.</summary>
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
                !db.WbSnapshotDecisions.Any(d => d.RunId == x.Id)).ExecuteDeleteAsync(ct);
            await db.WbArticleObservations.Where(x => x.ObservedAtUtc < cutoff).ExecuteDeleteAsync(ct);
            cleanedOn = today;
        }
        // Begin only at/after the existing nightly 06:15 Moscow slot.
        var slot = WbRefreshClock.LatestSlot(nowUtc, true);
        var window = new RecommendationWindow(WbSyncPlanner.Today(slot).AddDays(-15), WbSyncPlanner.Today(slot).AddDays(-2));
        foreach (var store in stores)
        {
            try
            {
                var campaigns = await db.Campaigns.AsNoTracking().Where(x => x.StoreId == store.Id &&
                    (x.WbStatus == 7 || x.WbStatus == 9 || x.WbStatus == 11)).ToListAsync(ct);
                if (store.AutoRefreshEnabled && !store.AutoRefreshBlocked && (store.TokenExpiresAtUtc == null || store.TokenExpiresAtUtc > nowUtc))
                {
                    await scope.ServiceProvider.GetRequiredService<AdvertisingSettingsSync>().RefreshAsync(store, nowUtc, ct);
                    var completed = await db.WbSyncJobs.AsNoTracking().Where(x => x.StoreId == store.Id && x.Kind == "clusters" &&
                        x.Status == "completed" && x.StartDate <= window.EndDate && x.EndDate >= window.StartDate)
                        .Select(x => new { x.StartDate, x.EndDate, x.PairIdsJson }).ToListAsync(ct);
                    var intervals = completed.Select(x => new ClusterCoverageInterval(x.StartDate, x.EndDate,
                        JsonSerializer.Deserialize<WbNormQueryPair[]>(x.PairIdsJson ?? "[]") ?? [])).ToArray();
                    var pairs = await (from stat in db.CampaignNomenclatureStatistics.AsNoTracking()
                        join campaign in db.Campaigns.AsNoTracking() on stat.CampaignId equals campaign.Id
                        join article in db.Nomenclatures.AsNoTracking() on stat.NomenclatureId equals article.Id
                        where campaign.StoreId == store.Id && stat.Date >= window.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) &&
                            stat.Date <= window.EndDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
                        select new { campaign.WbCampaignId, article.WbNomenclatureId }).Distinct().ToListAsync(ct);
                    for (var start = window.StartDate; start <= window.EndDate; start = start.AddDays(7))
                    {
                        var end = start.AddDays(6) < window.EndDate ? start.AddDays(6) : window.EndDate;
                        var missing = pairs.Any(pair => long.TryParse(pair.WbCampaignId, out var advert) &&
                            long.TryParse(pair.WbNomenclatureId, out var nm) && !RecommendationCoverage.Covers(new(start, end), advert, nm, intervals));
                        if (!missing) continue;
                        await scope.ServiceProvider.GetRequiredService<WbSyncPlanner>().EnqueueClustersAsync(store, start, end, null, ct);
                        break; // Existing queue serializes one cluster job per store.
                    }
                }
                foreach (var campaign in campaigns)
                    await scope.ServiceProvider.GetRequiredService<RecommendationService>().CalculateAsync(store.SellerId, campaign.Id, window, ct);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogError(error, "Recommendation calculation failed for store {StoreId}", store.Id);
            }
        }
    }
}
