using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Ecomads.WebApplication.Services.Wb;

// Reinterpret retained successful responses locally; never enqueue or call WB.
public sealed class WbSparseReplayWorker(IServiceScopeFactory scopes, TimeProvider clock,
    WbIngestionRollout rollout, ILogger<WbSparseReplayWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await RunOnceAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Sparse response recovery failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), clock, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
        var since = clock.GetUtcNow().UtcDateTime.AddDays(-31);
        var allowed = rollout.StoreIds;
        var ids = await db.WbResponseSnapshots.AsNoTracking().Where(x =>
                (allowed == null || allowed.Contains(x.StoreId)) &&
                (x.Kind == "fullstats" || x.Kind == "fullstats_month" || x.Kind == "fullstats_history" || x.Kind == "archive" || x.Kind == "clusters") &&
                x.RequestedAtUtc >= since && x.AppliedAtUtc != null &&
                (x.State == "applied" || x.State == "partial") && x.Version != WbIngestionVersion.Current &&
                x.HttpStatus >= 200 && x.HttpStatus < 300)
            .OrderByDescending(x => x.RequestedAtUtc).ThenBy(x => x.Id).Take(5).Select(x => x.Id).ToArrayAsync(ct);
        foreach (var id in ids)
        {
            try { await scope.ServiceProvider.GetRequiredService<WbResponseProcessor>().ApplyAsync(id, false, ct); }
            catch (Exception error) when (!ct.IsCancellationRequested &&
                error is JsonException or InvalidOperationException or FormatException or OverflowException)
            {
                db.ChangeTracker.Clear();
                // A malformed retained response gets one attempt, rather than blocking later ones.
                await db.WbResponseSnapshots.Where(x => x.Id == id && x.Version != WbIngestionVersion.Current)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "replay_failed")
                        .SetProperty(x => x.Reason, "sparse_recovery_error"), ct);
                logger.LogWarning(error, "Sparse response recovery skipped {SnapshotId}", id);
            }
        }
        return ids.Length;
    }
}
