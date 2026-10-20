using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Wb;

public sealed class WbProductOrdersRefresh(EcomadsDbContext db, WbSyncPlanner planner, TimeProvider clock)
{
    public static Task RequestManualAsync(EcomadsDbContext db, Guid storeId, DateTime now, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO wb_refresh_schedules (store_id, task, requested_slot_utc)
            VALUES ({storeId}, 'product_orders_manual', {now})
            ON CONFLICT (store_id, task) DO UPDATE SET requested_slot_utc = GREATEST(wb_refresh_schedules.requested_slot_utc, EXCLUDED.requested_slot_utc)", ct);

    // Bounded local recovery precedes any extra WB requests. No job state is replayed.
    public async Task RunAsync(Store store, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var endDay = WbSyncPlanner.Today(now).AddDays(-1);
        var candidates = await db.WbResponseSnapshots.AsNoTracking().Where(x => x.StoreId == store.Id &&
            WbProductOrdersImporter.Kinds.Contains(x.Kind) && x.AppliedAtUtc != null && x.State == "applied" &&
            x.ProductOrdersVersion < WbProductOrdersImporter.Version)
            .OrderByDescending(x => x.RequestedAtUtc).Select(x => new { x.Id, x.JobStateJson }).ToArrayAsync(ct);
        var pending = candidates.Where(x =>
        {
            try
            {
                var job = JsonSerializer.Deserialize<WbSyncJob>(x.JobStateJson);
                var ids = JsonSerializer.Deserialize<long[]>(job?.CampaignIdsJson ?? "[]") ?? [];
                if (job == null || job.NextCampaignOffset < 0 || job.NextCampaignOffset >= ids.Length) return true;
                var day = DateOnly.ParseExact(ids[job.NextCampaignOffset].ToString(), "yyyyMMdd");
                return day <= endDay && day.AddDays(1) >= endDay.AddDays(-29);
            }
            catch (Exception e) when (e is JsonException or FormatException) { return true; }
        }).Take(5).Select(x => x.Id).ToArray();
        foreach (var id in pending)
        {
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"wb-ingest:" + store.Id}, 0))", ct);
                var snapshot = await db.WbResponseSnapshots.SingleAsync(x => x.Id == id, ct);
                await new WbProductOrdersImporter(db).ApplyAsync(snapshot, ct);
                await tx.CommitAsync(ct);
            });
        }
        if (pending.Length > 0) return;
        // Serialize planners across instances; QueueAsync also has a uniqueness guard.
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({store.Id.ToString()}, 0))", ct);
            var current = await db.Stores.SingleAsync(x => x.Id == store.Id, ct);
            var request = await db.WbRefreshSchedules.FindAsync([store.Id, "product_orders_manual"], ct);
            var manual = request != null && (request.PlannedSlotUtc == null || request.RequestedSlotUtc > request.PlannedSlotUtc);
            if ((!current.AutoRefreshEnabled && !manual) || current.AutoRefreshBlocked || current.ApiKey == null || current.TokenExpiresAtUtc <= now) return;
            if (await db.WbSyncJobs.AnyAsync(x => x.StoreId == store.Id && x.Kind == "product_orders" &&
                (x.Status == "pending" || x.Status == "running"), ct)) return; // Includes user/stall pauses.
            var last = await db.WbSyncJobs.Where(x => x.StoreId == store.Id && x.Kind == "product_orders")
                .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
            // Access or payload errors require an explicit retry, never an endless auto-loop.
            if (last?.Status == "failed" && !manual) return;
            var slot = manual ? request!.RequestedSlotUtc : WbRefreshClock.LatestSlot(now, false);
            if (!manual && last != null && last.CreatedAtUtc >= slot) return;
            var end = WbSyncPlanner.Today(now).AddDays(-1); var start = end.AddDays(-29);
            var loaded = await db.WbProductOrderDays.Where(x => x.StoreId == store.Id && x.Date >= start && x.Date <= end)
                .ToDictionaryAsync(x => x.Date, ct);
            var quality = await db.WbSourceQualities.Where(x => x.StoreId == store.Id && x.Source == "product_orders" &&
                x.StartDate >= start && x.EndDate <= end).ToDictionaryAsync(x => x.StartDate, ct);
            bool Needs(DateOnly day) => !loaded.TryGetValue(day, out var row) ||
                !quality.TryGetValue(day, out var q) || !WbQuality.Confirmed(q.State) || q.AcceptedSnapshotId != row.SnapshotId ||
                (day >= end.AddDays(-6) && row.LoadedAtUtc < slot);
            var missing = Enumerable.Range(0, 30).Select(i => end.AddDays(-i)).Where(Needs).ToHashSet();
            var pairs = new List<long>();
            foreach (var day in missing.OrderByDescending(x => x).ToArray())
            {
                if (!missing.Remove(day)) continue;
                var past = day > start ? day.AddDays(-1) : day;
                missing.Remove(past); missing.Remove(past.AddDays(1));
                pairs.Add(long.Parse(past.ToString("yyyyMMdd")));
            }
            if (pairs.Count > 0) await planner.QueueAsync(store.Id, "product_orders", start, end,
                JsonSerializer.Serialize(pairs), null, null, ct);
            if (manual) { request!.PlannedSlotUtc = request.RequestedSlotUtc; await db.SaveChangesAsync(ct); }
            await tx.CommitAsync(ct);
        });
    }
}

public sealed class WbProductOrdersWorker(IServiceScopeFactory scopes, TimeProvider clock,
    WbIngestionRollout rollout, ILogger<WbProductOrdersWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
                var now = clock.GetUtcNow().UtcDateTime;
                var stores = await db.Stores.AsNoTracking().Where(x => x.ApiKey != null && !x.AutoRefreshBlocked &&
                    (x.TokenExpiresAtUtc == null || x.TokenExpiresAtUtc > now)).ToArrayAsync(ct);
                foreach (var store in stores.Where(x => rollout.Allows(x.Id)))
                {
                    try { await scope.ServiceProvider.GetRequiredService<WbProductOrdersRefresh>().RunAsync(store, ct); }
                    catch (Exception e) when (!ct.IsCancellationRequested) { logger.LogError(e, "Product order refresh failed for {StoreId}", store.Id); }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogError(e, "Product order worker failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), clock, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }
}
