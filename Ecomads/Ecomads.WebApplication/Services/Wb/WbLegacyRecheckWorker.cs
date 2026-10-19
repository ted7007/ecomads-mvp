using System.Text.Json;
using Ecomads.WebApplication.Data;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Wb;

public sealed class WbLegacyRecheckWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    TimeProvider clock, ILogger<WbLegacyRecheckWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (configuration.GetValue<int>("Wb:Ingestion:RecheckLegacyDays") <= 0) return;
        while (!ct.IsCancellationRequested)
        {
            try { await RunOnceAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "WB legacy recheck planning failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), clock, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var days = configuration.GetValue<int>("Wb:Ingestion:RecheckLegacyDays");
        if (days <= 0) return;
        if (days > 31) throw new InvalidOperationException("Перепроверка старых данных ограничена 31 закрытым днём.");
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
        var planner = scope.ServiceProvider.GetRequiredService<WbSyncPlanner>();
        var rollout = scope.ServiceProvider.GetRequiredService<WbIngestionRollout>();
        var now = clock.GetUtcNow().UtcDateTime;
        var end = WbDates.MoscowDay(now).AddDays(-1); var start = end.AddDays(1 - days);
        var allowed = rollout.StoreIds;
        var stores = await db.Stores.AsNoTracking().Where(x => x.ApiKey != null && x.AutoRefreshEnabled &&
            !x.AutoRefreshBlocked && (x.TokenExpiresAtUtc == null || x.TokenExpiresAtUtc > now) &&
            (allowed == null || allowed.Contains(x.Id))).Select(x => x.Id).ToArrayAsync(ct);
        foreach (var store in stores)
        {
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({store.ToString()}, 0))", ct);
                var sources = await db.WbSourceQualities.Where(x => x.StoreId == store && x.State == "legacy" &&
                    x.StartDate <= end && x.EndDate >= start).Select(x => x.Source).Distinct().ToArrayAsync(ct);
                foreach (var source in sources.Where(x => x is "expenses" or "orders" or "fullstats"))
                {
                    var kind = source == "expenses" ? "expenses_month" : source == "orders" ? "funnel_month" : "fullstats_month";
                    if (await db.WbSyncJobs.AnyAsync(x => x.StoreId == store && x.Kind == kind &&
                        (x.Status == "pending" || x.Status == "running"), ct)) continue;
                    // A failed recheck is visible and waits for the normal retry action;
                    // this planner must not create an endless stream of replacement jobs.
                    if (await db.WbSyncJobs.AnyAsync(x => x.StoreId == store && x.Kind == kind &&
                        x.StartDate == start && x.EndDate == end && x.CreatedAtUtc >= start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), ct)) continue;
                    long[] ids = []; string? pairs = null;
                    if (source == "expenses") pairs = JsonSerializer.Serialize(WbSyncPlanner.ExpenseHistoryBatches(start, end, new HashSet<DateOnly>()));
                    else if (source == "fullstats")
                    {
                        var campaigns = await db.Campaigns.Where(x => x.StoreId == store &&
                            (x.WbStatus == 7 || x.WbStatus == 9 || x.WbStatus == 11)).ToArrayAsync(ct);
                        ids = campaigns.Where(x => !WbCampaignEligibility.CreatedAfter(x, end) && !WbCampaignEligibility.FinishedBefore(x, start))
                            .Select(x => long.TryParse(x.WbCampaignId, out var id) ? id : 0).Where(x => x > 0).ToArray();
                        if (ids.Length == 0) continue;
                    }
                    else
                    {
                        var dates = new List<long>();
                        for (var date = start; date <= end; date = date.AddDays(2))
                        {
                            var pairStart = date == end && days > 1 ? date.AddDays(-1) : date;
                            dates.Add(long.Parse(pairStart.ToString("yyyyMMdd")));
                        }
                        ids = dates.Distinct().ToArray();
                    }
                    await planner.QueueAsync(store, kind, start, end, JsonSerializer.Serialize(ids), pairs, null, ct);
                    var slot = WbRefreshClock.LatestSlot(now, true);
                    var schedule = await db.WbRefreshSchedules.FindAsync([store, kind], ct);
                    if (schedule == null) db.WbRefreshSchedules.Add(new() { StoreId = store, Task = kind, RequestedSlotUtc = slot, PlannedSlotUtc = slot });
                    else if (schedule.PlannedSlotUtc == null || schedule.PlannedSlotUtc < slot)
                    { schedule.RequestedSlotUtc = slot; schedule.PlannedSlotUtc = slot; }
                    logger.LogInformation("Queued WB {Kind} legacy recheck for store {StoreId}, {Start} through {End}", kind, store, start, end);
                }
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            });
        }
    }
}
