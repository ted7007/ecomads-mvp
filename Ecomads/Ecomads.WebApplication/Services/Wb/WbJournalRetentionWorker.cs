using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Wb;

public sealed class WbJournalRetentionWorker(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<WbJournalRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await PruneAsync(scope.ServiceProvider.GetRequiredService<EcomadsDbContext>(), clock.GetUtcNow().UtcDateTime, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "WB journal retention failed"); }
            try { await Task.Delay(TimeSpan.FromDays(1), clock, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }

    public static async Task PruneAsync(EcomadsDbContext db, DateTime now, CancellationToken ct)
    {
        // Serialize retention with publication/acceptance for each store. Read metadata only.
        var stores = await db.Stores.Select(x => x.Id).ToArrayAsync(ct);
        foreach (var store in stores)
        {
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"wb-ingest:" + store}, 0))", ct);
                var rows = await db.WbResponseSnapshots.AsNoTracking().Where(x => x.StoreId == store)
                    .Select(x => new { x.Id, x.State, x.ReceivedAtUtc, x.JobStateJson }).ToArrayAsync(ct);
                var quality = await db.WbSourceQualities.AsNoTracking().Where(x => x.StoreId == store)
                    .Select(x => new { x.SnapshotId, x.AcceptedSnapshotId }).ToArrayAsync(ct);
                var keep = quality.SelectMany(x => new[] { x.SnapshotId, x.AcceptedSnapshotId })
                    .Where(x => x.HasValue).Select(x => x!.Value).ToHashSet();
                keep.UnionWith(await db.WbIngestionDecisions.Where(x => x.StoreId == store).Select(x => x.SnapshotId).ToArrayAsync(ct));
                // Unprocessed responses remain recoverable. Unresolved/current observations
                // are protected by quality references above, including the previous accepted
                // snapshot. Superseded errors and partial responses expire after 90 days.
                keep.UnionWith(rows.Where(x => x.State == "received" || x.ReceivedAtUtc >= now.AddDays(-90)).Select(x => x.Id));
                var states = await db.WbSyncJobs.Where(x => x.StoreId == store && x.Status != "completed")
                    .Select(x => x.PairIdsJson).ToArrayAsync(ct);
                void KeepPages(string? state)
                {
                    if (state == null || !state.TrimStart().StartsWith('{')) return;
                    var page = JsonSerializer.Deserialize<WbFunnelPageState>(state);
                    if (page?.SnapshotIds != null) keep.UnionWith(page.SnapshotIds);
                }
                foreach (var state in states) KeepPages(state);
                var inspected = new HashSet<Guid>();
                while (true)
                {
                    var parents = rows.Where(x => keep.Contains(x.Id) && inspected.Add(x.Id)).ToArray();
                    if (parents.Length == 0) break;
                    foreach (var parent in parents)
                        KeepPages(JsonSerializer.Deserialize<WbSyncJob>(parent.JobStateJson)?.PairIdsJson);
                }
                var remove = rows.Where(x => !keep.Contains(x.Id)).Select(x => x.Id).ToArray();
                if (remove.Length > 0) await db.WbResponseSnapshots.Where(x => remove.Contains(x.Id)).ExecuteDeleteAsync(ct);
                await tx.CommitAsync(ct);
            });
        }
    }
}
