using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Wb;

public sealed class WbScheduledRefreshService(EcomadsDbContext db, WbSyncPlanner planner)
{
    private static readonly string[] Tasks = ["campaign_list", "fullstats", "expenses", "funnel_recent",
        "fullstats_month", "expenses_month", "funnel_month", "details", "history"];

    public async Task RunAsync(Guid storeId, DateTime nowUtc, CancellationToken ct)
    {
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Serialize schedule planning across processes. Job uniqueness also covers manual requests.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({storeId.ToString()}, 0))", ct);
            db.ChangeTracker.Clear();
            var store = await db.Stores.SingleAsync(x => x.Id == storeId, ct);
            if (store.ApiKey == null || !store.AutoRefreshEnabled || store.AutoRefreshBlocked ||
                store.TokenExpiresAtUtc <= nowUtc) return;
            var schedules = await db.WbRefreshSchedules.Where(x => x.StoreId == storeId).ToDictionaryAsync(x => x.Task, ct);
            foreach (var task in Tasks)
            {
                var slot = WbRefreshClock.LatestSlot(nowUtc, task is "fullstats_month" or "expenses_month" or "funnel_month" or "details" or "history");
                if (!schedules.TryGetValue(task, out var schedule))
                {
                    schedule = new WbRefreshSchedule { StoreId = storeId, Task = task, RequestedSlotUtc = slot };
                    db.WbRefreshSchedules.Add(schedule);
                }
                if (slot > schedule.RequestedSlotUtc) schedule.RequestedSlotUtc = slot;
                if (schedule.PlannedSlotUtc >= schedule.RequestedSlotUtc) continue;
                if (task == "campaign_list")
                {
                    await planner.RefreshCampaignsAsync(store, ct);
                    if (store.AutoRefreshBlocked) break;
                    if (store.CampaignsRefreshedAtUtc < schedule.RequestedSlotUtc || store.CampaignsRefreshedAtUtc == null) continue;
                }
                else if (task == "history")
                {
                    // Existing paused jobs are returned as-is; never resume a user pause.
                    var end = WbSyncPlanner.Today(nowUtc).AddDays(-1);
                    await planner.EnqueueYearFullStatsAsync(store, end.AddDays(-364), end, ct);
                    await planner.EnqueueYearExpensesAsync(store, end.AddDays(-364), end, ct);
                    await planner.EnqueueYearOrdersAsync(store, end.AddDays(-364), end, ct);
                    store.AutoHistoryStartedAtUtc ??= nowUtc;
                }
                else if (task == "details")
                {
                    var end = WbSyncPlanner.Today(nowUtc).AddDays(-1);
                    if (!await db.WbSyncJobs.AnyAsync(x => x.StoreId == storeId &&
                        (x.Kind == "fullstats" || x.Kind == "fullstats_month") && x.Status == "completed" &&
                        x.StartDate <= end.AddDays(-6) && x.EndDate >= end && x.CompletedAtUtc >= schedule.RequestedSlotUtc, ct)) continue;
                    if (await db.WbSyncJobs.AnyAsync(x => x.StoreId == storeId &&
                        (x.Kind == "clusters" || x.Kind == "jam") && (x.Status == "pending" || x.Status == "running"), ct)) continue;
                    var runId = Guid.NewGuid();
                    await planner.EnqueueClustersAsync(store, runId, ct);
                    if (store.JamStatus is not ("access_denied" or "payment_required"))
                        await planner.EnqueueJamAsync(store, runId, ct);
                }
                else
                {
                    if (await db.WbSyncJobs.AnyAsync(x => x.StoreId == storeId && x.Kind == task &&
                        (x.Status == "pending" || x.Status == "running"), ct)) continue;
                    if (store.CampaignsRefreshedAtUtc == null && task.StartsWith("fullstats")) continue;
                    var job = await QueueSourceAsync(store, task, nowUtc, ct);
                    if (job != null && job.CreatedAtUtc < schedule.RequestedSlotUtc) continue;
                }
                schedule.PlannedSlotUtc = schedule.RequestedSlotUtc;
                await db.SaveChangesAsync(ct);
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });
    }

    private async Task<WbSyncJob?> QueueSourceAsync(Store store, string task, DateTime nowUtc, CancellationToken ct)
    {
        var today = WbSyncPlanner.Today(nowUtc);
        var monthly = task.EndsWith("_month");
        var end = monthly ? today.AddDays(-1) : today;
        var start = end.AddDays(monthly ? -29 : -6);
        long[] ids = [];
        string? pairs = null;
        if (task.StartsWith("fullstats"))
        {
            var campaigns = await db.Campaigns.Where(x => x.StoreId == store.Id &&
                (x.WbStatus == 7 || x.WbStatus == 9 || x.WbStatus == 11)).ToListAsync(ct);
            ids = campaigns.Where(x => !WbCampaignEligibility.FinishedBefore(x, start) &&
                !WbCampaignEligibility.CreatedAfter(x, end)).OrderBy(x => x.WbStatus == 9 ? 0 : 1)
                .Select(x => long.TryParse(x.WbCampaignId, out var id) ? id : 0).Where(x => x > 0).ToArray();
            if (ids.Length == 0) return null;
        }
        else if (task == "expenses_month")
            pairs = JsonSerializer.Serialize(WbSyncPlanner.ExpenseHistoryBatches(start, end, new HashSet<DateOnly>()));
        else if (task == "funnel_month")
        {
            // The operational grouped-history query covers today through today-6.
            end = today.AddDays(-7);
            ids = Enumerable.Range(0, (end.DayNumber - start.DayNumber) / 2 + 1)
                .Select(i => long.Parse(start.AddDays(i * 2).ToString("yyyyMMdd"))).ToArray();
        }
        return await planner.QueueAsync(store.Id, task, start, end, JsonSerializer.Serialize(ids), pairs, null, ct);
    }
}
