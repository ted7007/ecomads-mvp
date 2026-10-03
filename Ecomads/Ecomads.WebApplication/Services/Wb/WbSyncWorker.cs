using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Wb;

public sealed class WbSyncWorker(IServiceScopeFactory scopes, ILogger<WbSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "WB sync worker failed before completing a job step");
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ProcessNextAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
        var job = await db.WbSyncJobs
            .Where(x => (x.Status == "pending" || x.Status == "running") && x.NextAttemptAtUtc <= DateTime.UtcNow)
            .OrderBy(x => x.Kind == "fullstats" || x.Kind == "funnel_recent" ? 0 :
                x.Kind == "clusters" || x.Kind == "jam" ? 1 : 2)
            .ThenBy(x => x.NextAttemptAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (job == null) return;

        var store = await db.Stores.SingleOrDefaultAsync(x => x.Id == job.StoreId, cancellationToken);
        if (store?.ApiKey == null)
        {
            job.Status = "failed";
            job.Stage = "failed";
            job.ErrorCode = "token_missing";
            job.UpdatedAtUtc = DateTime.UtcNow;
            job.CompletedAtUtc = job.UpdatedAtUtc;
            Record(db, job);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var ids = JsonSerializer.Deserialize<long[]>(job.CampaignIdsJson) ?? [];
        var pairs = job.Kind == "clusters"
            ? JsonSerializer.Deserialize<WbNormQueryPair[]>(job.PairIdsJson ?? "[]") ?? []
            : [];
        var total = WbSyncJobUnits.Total(job);
        if (job.NextCampaignOffset >= total)
        {
            Complete(job, store);
            Record(db, job);
            await db.SaveChangesAsync(cancellationToken);
            if (job.Status == "completed")
                await PlanFollowupsAsync(scope.ServiceProvider, job, store, cancellationToken);
            return;
        }

        var now = DateTime.UtcNow;
        var reserved = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var reservation = await db.Database.BeginTransactionAsync(cancellationToken);
            if (!await WbRateLimits.TryReserveAsync(db, store.Id, job.Kind, job.NextCampaignOffset, now, cancellationToken))
            {
                job.NextAttemptAtUtc = await WbRateLimits.NextSlotAsync(db, store.Id,
                    WbRateLimits.MethodFor(job.Kind, job.NextCampaignOffset), now, cancellationToken);
                job.Stage = "waiting";
                job.WaitReason = "rate_limit";
                await db.SaveChangesAsync(cancellationToken);
                await reservation.CommitAsync(cancellationToken);
                return false;
            }
            job.Status = "running";
            job.Stage = "requesting";
            job.WaitReason = null;
            job.StartedAtUtc ??= now;
            job.NextAttemptAtUtc = now.Add(WbRateLimits.IntervalFor(job.Kind));
            job.LastRequestAtUtc = now;
            job.UpdatedAtUtc = now;
            Record(db, job);
            // Reserve the slot before the HTTP request. A crash may delay a retry but cannot spend the same slot twice.
            await db.SaveChangesAsync(cancellationToken);
            await reservation.CommitAsync(cancellationToken);
            return true;
        });
        if (!reserved) return;

        var wb = scope.ServiceProvider.GetRequiredService<IWbPromotionClient>();
        var tokens = scope.ServiceProvider.GetRequiredService<IWbTokenService>();
        var batch = ids.Skip(job.NextCampaignOffset).Take(WbSyncJobUnits.BatchSize(job.Kind)).ToArray();
        var pairBatch = pairs.Skip(job.NextCampaignOffset).Take(WbSyncJobUnits.BatchSize(job.Kind)).ToArray();
        try
        {
            var token = tokens.Unprotect(store.ApiKey);
            if (job.Kind is "funnel" or "funnel_recent" or "funnel_backfill")
            {
                var funnel = scope.ServiceProvider.GetRequiredService<IWbSalesFunnelClient>();
                var importer = scope.ServiceProvider.GetRequiredService<WbSalesFunnelImporter>();
                if (job.Kind == "funnel_recent" || (job.Kind == "funnel" && job.NextCampaignOffset == 0))
                {
                    using var response = await funnel.GetGroupedHistoryAsync(token, job.StartDate, job.EndDate,
                        cancellationToken);
                    job.Stage = "importing";
                    job.UpdatedAtUtc = DateTime.UtcNow;
                    Record(db, job);
                    await db.SaveChangesAsync(cancellationToken);
                    await importer.ImportAsync(store.Id, job.StartDate, job.EndDate, response.RootElement, cancellationToken);
                    job.NextCampaignOffset = 1;
                }
                else
                {
                    var pairIndex = job.Kind == "funnel" ? job.NextCampaignOffset - 1 : job.NextCampaignOffset;
                    var pairStart = DateOnly.ParseExact(ids[pairIndex].ToString(), "yyyyMMdd");
                    var state = JsonSerializer.Deserialize<WbFunnelPageState>(job.PairIdsJson ?? "null") ?? new();
                    using var response = await funnel.GetProductsAsync(token, pairStart.AddDays(1), pairStart,
                        state.Offset, cancellationToken);
                    var page = WbSalesFunnelImporter.ReadProductsPage(response.RootElement, state);
                    job.Stage = "importing";
                    job.UpdatedAtUtc = DateTime.UtcNow;
                    Record(db, job);
                    await db.SaveChangesAsync(cancellationToken);
                    if (page.ProductCount == 1000)
                        job.PairIdsJson = JsonSerializer.Serialize(page.State);
                    else
                    {
                        await importer.ImportProductsAsync(store.Id, pairStart.AddDays(1), pairStart,
                            page.State, cancellationToken);
                        job.PairIdsJson = null;
                        job.NextCampaignOffset++;
                    }
                }
            }
            else if (job.Kind == "clusters")
            {
                using var response = await wb.GetNormQueryStatsAsync(token, pairBatch,
                    job.StartDate, job.EndDate, cancellationToken);
                job.Stage = "importing";
                job.UpdatedAtUtc = DateTime.UtcNow;
                Record(db, job);
                await db.SaveChangesAsync(cancellationToken);
                var importer = scope.ServiceProvider.GetRequiredService<WbNormQueryImporter>();
                await importer.ImportAsync(store.Id, pairBatch, job.StartDate, job.EndDate,
                    response.RootElement, cancellationToken);
                job.NextCampaignOffset += pairBatch.Length;
            }
            else if (job.Kind == "jam")
            {
                var jam = scope.ServiceProvider.GetRequiredService<IWbJamClient>();
                using var response = await jam.GetSearchTextsAsync(token, batch,
                    job.StartDate, job.EndDate, cancellationToken);
                job.Stage = "importing";
                job.UpdatedAtUtc = DateTime.UtcNow;
                Record(db, job);
                await db.SaveChangesAsync(cancellationToken);
                var importer = scope.ServiceProvider.GetRequiredService<WbJamImporter>();
                var imported = await importer.ImportAsync(store.Id, batch, job.StartDate, job.EndDate,
                    response.RootElement, cancellationToken, job.Id);
                job.ImportedRows += imported.Rows;
                job.ItemsWithData += imported.WithData;
                job.ItemsWithoutData += imported.WithoutData;
                job.NextCampaignOffset += batch.Length;
                store.JamStatus = "active";
                store.JamCheckedAtUtc = DateTime.UtcNow;
            }
            else
            {
                using var response = await wb.GetFullStatsAsync(token, batch,
                    job.StartDate, job.EndDate, cancellationToken);
                job.Stage = "importing";
                job.UpdatedAtUtc = DateTime.UtcNow;
                Record(db, job);
                await db.SaveChangesAsync(cancellationToken);
                var importer = scope.ServiceProvider.GetRequiredService<WbFullStatsImporter>();
                var imported = await importer.ImportAsync(store.Id, response.RootElement, cancellationToken,
                    batch, job.Id);
                if (response.RootElement.ValueKind == JsonValueKind.Null)
                {
                    logger.LogWarning("WB fullstats returned JSON null for job {JobId}, batch size {BatchSize}",
                        job.Id, batch.Length);
                    job.ErrorCode = "wb_null_response";
                    job.UpdatedAtUtc = DateTime.UtcNow;
                    Record(db, job);
                }
                job.ImportedRows += imported.Rows;
                job.ItemsWithData += batch.Length - imported.MissingCampaignIds.Length;
                job.ItemsWithoutData += imported.MissingCampaignIds.Length;
                var retried = JsonSerializer.Deserialize<long[]>(job.PairIdsJson ?? "[]")?.ToHashSet() ?? [];
                var retryIds = imported.MissingCampaignIds.Where(x => retried.Add(x)).ToArray();
                if (retryIds.Length > 0)
                {
                    job.CampaignIdsJson = JsonSerializer.Serialize(ids.Concat(retryIds).ToArray());
                    job.PairIdsJson = JsonSerializer.Serialize(retried.ToArray());
                }
                job.NextCampaignOffset += batch.Length;
            }
            job.AttemptCount = 0;
            job.ErrorCode = null;
            job.UpdatedAtUtc = DateTime.UtcNow;
            if (job.NextCampaignOffset >= WbSyncJobUnits.Total(job)) Complete(job, store);
            else { job.Stage = "waiting"; job.WaitReason = "rate_limit"; }
            Record(db, job);
            await db.SaveChangesAsync(cancellationToken);
            if (job.Kind == "fullstats" || (job.Status == "completed" && (job.Kind is "clusters" or "jam")))
                await PlanFollowupsAsync(scope.ServiceProvider, job, store, cancellationToken);
        }
        catch (WbApiException error)
        {
            if (job.Kind == "jam" && error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.PaymentRequired)
            {
                store.JamStatus = error.StatusCode == HttpStatusCode.PaymentRequired ? "payment_required" : "access_denied";
                store.JamCheckedAtUtc = DateTime.UtcNow;
            }
            // A quota response means "wait", not that the payload is broken.
            if (error.StatusCode != HttpStatusCode.TooManyRequests) job.AttemptCount++;
            job.ErrorCode = $"wb_{(int)error.StatusCode}";
            job.UpdatedAtUtc = DateTime.UtcNow;
            if (((int)error.StatusCode is >= 400 and < 500 && error.StatusCode != HttpStatusCode.TooManyRequests)
                || job.AttemptCount >= 3)
            {
                job.Status = "failed";
                job.Stage = "failed";
                job.CompletedAtUtc = job.UpdatedAtUtc;
            }
            else
            {
                job.Stage = "waiting";
                job.WaitReason = error.StatusCode == HttpStatusCode.TooManyRequests ? "rate_limit" : "retry";
                if (error.RetryAfter.HasValue)
                {
                    var retryAt = DateTime.UtcNow.Add(error.RetryAfter.Value);
                    if (retryAt > job.NextAttemptAtUtc) job.NextAttemptAtUtc = retryAt;
                    await WbRateLimits.DeferUntilAsync(db, store.Id, job.Kind,
                        job.NextCampaignOffset, retryAt, cancellationToken);
                }
            }
            Record(db, job);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception error) when (error is JsonException or HttpRequestException or InvalidOperationException or CryptographicException)
        {
            logger.LogWarning(error, "WB sync step failed for job {JobId}", job.Id);
            job.AttemptCount++;
            job.ErrorCode = error is JsonException ? "invalid_response"
                : error is CryptographicException ? "token_unreadable"
                : error is InvalidOperationException ? "internal_error" : "transport_error";
            job.UpdatedAtUtc = DateTime.UtcNow;
            if (job.AttemptCount >= 3 || error is CryptographicException ||
                (error is JsonException && job.AttemptCount >= 2))
            {
                job.Status = "failed";
                job.Stage = "failed";
                job.CompletedAtUtc = job.UpdatedAtUtc;
            }
            else { job.Stage = "waiting"; job.WaitReason = "retry"; }
            Record(db, job);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static void Complete(WbSyncJob job, Store store)
    {
        job.Status = "completed";
        job.Stage = "completed";
        job.WaitReason = null;
        job.UpdatedAtUtc = DateTime.UtcNow;
        job.CompletedAtUtc = job.UpdatedAtUtc;
        if (job.Kind == "fullstats") store.LastSyncAt = job.UpdatedAtUtc;
    }

    private async Task PlanFollowupsAsync(IServiceProvider services, WbSyncJob job, Store store,
        CancellationToken cancellationToken)
    {
        if (job.Kind is not ("fullstats" or "clusters" or "jam") || job.RunId == null) return;
        try
        {
            var planner = services.GetRequiredService<WbSyncPlanner>();
            if (job.Kind != "clusters") await planner.EnqueueClustersAsync(store, job.RunId.Value, cancellationToken);
            if (job.Kind != "jam") await planner.EnqueueJamAsync(store, job.RunId.Value, cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError(error, "Could not plan WB follow-up jobs for run {RunId}", job.RunId);
        }
    }

    private static void Record(EcomadsDbContext db, WbSyncJob job) => db.WbSyncJobEvents.Add(new WbSyncJobEvent
    {
        JobId = job.Id, OccurredAtUtc = job.UpdatedAtUtc, Stage = job.Stage,
        ErrorCode = job.ErrorCode, ProcessedCount = job.NextCampaignOffset,
        AttemptNumber = job.AttemptCount
    });
}
