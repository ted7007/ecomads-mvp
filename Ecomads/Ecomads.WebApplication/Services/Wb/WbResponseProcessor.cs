using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ecomads.WebApplication.Services.Wb;

public sealed class WbResponseImportException(string reason, Exception inner) : JsonException("Ответ WB не соответствует ограничениям хранилища.", inner)
{
    public string ReasonCode { get; } = reason;
}

public sealed class WbResponseProcessor(EcomadsDbContext db, IServiceProvider services, TimeProvider clock)
{
    public async Task ApplyAsync(Guid snapshotId, bool advanceJob, CancellationToken ct)
    {
        try { await ApplyCoreAsync(snapshotId, advanceJob, ct); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException pg &&
            !pg.IsTransient && (pg.SqlState.StartsWith("22", StringComparison.Ordinal) || pg.SqlState.StartsWith("23", StringComparison.Ordinal)))
        {
            // A permanent input/constraint failure must not poison the recoverable-response
            // queue forever. The raw response remains available for a parser fix and replay.
            throw new WbResponseImportException("storage_constraint", error);
        }
    }

    private async Task ApplyCoreAsync(Guid snapshotId, bool advanceJob, CancellationToken ct)
    {
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var snapshot = await db.WbResponseSnapshots.SingleAsync(x => x.Id == snapshotId, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"wb-ingest:" + snapshot.StoreId}, 0))", ct);
            await db.Entry(snapshot).ReloadAsync(ct);
            if (db.Entry(snapshot).State == EntityState.Detached)
                throw new InvalidOperationException("Сохранённый ответ больше недоступен.");
            var bodyBytes = snapshot.GetBodyBytes();
            if (snapshot.Sha256 != Convert.ToHexString(SHA256.HashData(bodyBytes)))
                throw new InvalidOperationException("Контрольная сумма сохранённого ответа WB не совпадает.");
            if (snapshot.AppliedAtUtc != null && snapshot.Version == WbIngestionVersion.Current) return;
            if (snapshot.HttpStatus is not (>= 200 and < 300) || snapshot.Reason == "transport_error")
                throw new InvalidOperationException("Ответ с ошибкой транспорта нельзя импортировать.");
            var original = JsonSerializer.Deserialize<WbSyncJob>(snapshot.JobStateJson)!;
            var job = JsonSerializer.Deserialize<WbSyncJob>(snapshot.JobStateJson)!;
            using var context = new WbIngestionContext(job, snapshot.RequestedAtUtc) {
                SnapshotId = snapshot.Id, ReceivedAtUtc = snapshot.ReceivedAtUtc };
            var bomLength = bodyBytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
            using var document = JsonDocument.Parse(bodyBytes.AsMemory(bomLength));
            var root = document.RootElement;
            var ids = JsonSerializer.Deserialize<long[]>(job.CampaignIdsJson) ?? [];
            var offset = job.NextCampaignOffset;
            var start = job.StartDate; var end = job.EndDate;
            if (job.Kind is "expenses" or "expenses_month" or "expenses_history")
            {
                var periods = JsonSerializer.Deserialize<WbHistoryBatch[]>(job.PairIdsJson ?? "[]") ?? [];
                if (periods.Length > 0) { start = periods[offset].StartDate; end = periods[offset].EndDate; }
                job.ImportedRows += await services.GetRequiredService<WbCostsImporter>().ImportAsync(job.StoreId, start, end,
                    root, ct, allowAdjacentDays: true, fillMissingOnly: job.Kind == "expenses_history");
                job.NextCampaignOffset++;
            }
            else if (job.Kind is "fullstats" or "fullstats_month" or "fullstats_history" or "archive")
            {
                var batch = ids.Skip(offset).Take(50).ToArray();
                if (job.Kind == "fullstats_history")
                {
                    var period = JsonSerializer.Deserialize<WbHistoryBatch[]>(job.PairIdsJson!)![offset];
                    batch = period.CampaignIds; start = period.StartDate; end = period.EndDate;
                }
                if (root.ValueKind != JsonValueKind.Array) throw new JsonException("Статистика WB не является массивом.");
                var imported = await services.GetRequiredService<WbFullStatsImporter>().ImportAsync(job.StoreId, root, ct,
                    batch, job.Id, fillMissingOnly: job.Kind == "fullstats_history", requestedStart: start, requestedEnd: end);
                job.ImportedRows += imported.Rows;
                job.ItemsWithData += batch.Length - imported.MissingCampaignIds.Length;
                job.ItemsWithoutData += imported.MissingCampaignIds.Length;
                job.NextCampaignOffset += job.Kind == "fullstats_history" ? 1 : batch.Length;
            }
            else if (job.Kind == "funnel_recent")
            {
                await services.GetRequiredService<WbSalesFunnelImporter>().ImportAsync(job.StoreId, start, end, root, ct);
                job.NextCampaignOffset = 1;
            }
            else if (job.Kind is "funnel_backfill" or "funnel_month" or "funnel_year" or "product_orders")
            {
                var day = DateOnly.ParseExact(ids[offset].ToString(), "yyyyMMdd");
                var state = JsonSerializer.Deserialize<WbFunnelPageState>(job.PairIdsJson ?? "null") ?? new();
                var page = WbSalesFunnelImporter.ReadProductsPage(root, state);
                if (page.ProductCount == 1000) job.PairIdsJson = JsonSerializer.Serialize(page.State);
                else
                {
                    await services.GetRequiredService<WbSalesFunnelImporter>().ImportProductsAsync(job.StoreId, day.AddDays(1), day,
                        page.State, ct, replaceExisting: job.Kind == "funnel_month", snapshotAtUtc: page.State.StartedAtUtc);
                    job.PairIdsJson = null; job.NextCampaignOffset++;
                }
                await new WbProductOrdersImporter(db).ApplyAsync(snapshot, ct);
                if (snapshot.ProductOrdersError != null) job.ItemsWithoutData++;
            }
            else if (job.Kind == "clusters")
            {
                var pairs = JsonSerializer.Deserialize<WbNormQueryPair[]>(job.PairIdsJson!)!.Skip(offset).Take(100).ToArray();
                await services.GetRequiredService<WbNormQueryImporter>().ImportAsync(job.StoreId, pairs, start, end, root, ct);
                job.NextCampaignOffset += pairs.Length;
            }
            else if (job.Kind == "jam")
            {
                var batch = ids.Skip(offset).Take(50).ToArray();
                var imported = await services.GetRequiredService<WbJamImporter>().ImportAsync(job.StoreId, batch, start, end, root, ct, job.Id);
                job.ImportedRows += imported.Rows; job.ItemsWithData += imported.WithData; job.ItemsWithoutData += imported.WithoutData;
                job.NextCampaignOffset += batch.Length;
            }
            else if (job.Kind == "campaign_list")
            {
                var store = await db.Stores.SingleAsync(x => x.Id == job.StoreId, ct);
                await services.GetRequiredService<WbSyncPlanner>().UpsertCampaignsAsync(store, WbPromotionClient.ReadCampaigns(root), ct);
            }
            else throw new InvalidOperationException("Неизвестный тип сохранённого ответа.");
            snapshot.State = context.HasIssues ? "partial" : "applied";
            snapshot.Reason = context.Issues.Count > 0 ? JsonSerializer.Serialize(context.Issues) : context.HasIssues ? "source_quality" : null;
            snapshot.Version = WbIngestionVersion.Current; snapshot.AppliedAtUtc = clock.GetUtcNow().UtcDateTime;
            if (advanceJob && job.Id != Guid.Empty)
            {
                var current = await db.WbSyncJobs.SingleAsync(x => x.Id == job.Id, ct);
                if (current.NextCampaignOffset == offset && WbJobState.SameJson(current.PairIdsJson, original.PairIdsJson) &&
                    WbJobState.SameJson(current.CampaignIdsJson, original.CampaignIdsJson) &&
                    current.Status is "pending" or "running" or "failed")
                {
                    current.NextCampaignOffset = job.NextCampaignOffset; current.PairIdsJson = job.PairIdsJson;
                    current.ImportedRows = job.ImportedRows; current.ItemsWithData = job.ItemsWithData; current.ItemsWithoutData = job.ItemsWithoutData;
                    current.AttemptCount = 0; current.ErrorCode = null; current.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
                    current.LastProgressAtUtc = current.UpdatedAtUtc;
                    current.Status = current.NextCampaignOffset >= WbSyncJobUnits.Total(current) ? "completed" : "running";
                    current.Stage = current.Status == "completed" ? "completed" : "waiting";
                    current.WaitReason = current.Status == "completed" ? null : "rate_limit";
                    current.CompletedAtUtc = current.Status == "completed" ? current.UpdatedAtUtc : null;
                    db.WbSyncJobEvents.Add(new() { JobId = current.Id, Stage = current.Stage,
                        OccurredAtUtc = current.UpdatedAtUtc, ProcessedCount = current.NextCampaignOffset });
                    var store = await db.Stores.SingleAsync(x => x.Id == job.StoreId, ct);
                    if (current.Status == "completed" && job.Kind is "fullstats" or "fullstats_month") store.LastSyncAt = current.UpdatedAtUtc;
                    if (job.Kind == "jam") { store.JamStatus = "active"; store.JamCheckedAtUtc = current.UpdatedAtUtc; }
                }
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }
}
