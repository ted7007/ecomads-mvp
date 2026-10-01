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
            .OrderBy(x => x.NextAttemptAtUtc)
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

        var wb = scope.ServiceProvider.GetRequiredService<IWbPromotionClient>();
        var tokens = scope.ServiceProvider.GetRequiredService<IWbTokenService>();
        var batch = ids.Skip(job.NextCampaignOffset).Take(WbSyncJobUnits.BatchSize(job.Kind)).ToArray();
        var pairBatch = pairs.Skip(job.NextCampaignOffset).Take(WbSyncJobUnits.BatchSize(job.Kind)).ToArray();
        try
        {
            var token = tokens.Unprotect(store.ApiKey);
            if (job.Kind == "funnel")
            {
                var funnel = scope.ServiceProvider.GetRequiredService<IWbSalesFunnelClient>();
                using var response = await funnel.GetGroupedHistoryAsync(token, job.StartDate, job.EndDate,
                    cancellationToken);
                job.Stage = "importing";
                job.UpdatedAtUtc = DateTime.UtcNow;
                Record(db, job);
                await db.SaveChangesAsync(cancellationToken);
                var importer = scope.ServiceProvider.GetRequiredService<WbSalesFunnelImporter>();
                await importer.ImportAsync(store.Id, job.StartDate, job.EndDate, response.RootElement, cancellationToken);
                job.NextCampaignOffset = 1;
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
                await importer.ImportAsync(store.Id, batch, job.StartDate, job.EndDate,
                    response.RootElement, cancellationToken);
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
                await importer.ImportAsync(store.Id, response.RootElement, cancellationToken);
                job.NextCampaignOffset += batch.Length;
            }
            job.AttemptCount = 0;
            job.ErrorCode = null;
            job.UpdatedAtUtc = DateTime.UtcNow;
            if (job.NextCampaignOffset >= total) Complete(job, store);
            else { job.Stage = "waiting"; job.WaitReason = "rate_limit"; }
            Record(db, job);
            await db.SaveChangesAsync(cancellationToken);
            if (job.Status == "completed")
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
            if (job.AttemptCount >= 3 || error is JsonException or CryptographicException)
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
        if (job.Kind != "fullstats" || job.RunId == null) return;
        try
        {
            var planner = services.GetRequiredService<WbSyncPlanner>();
            await planner.EnqueueClustersAsync(store, job.RunId.Value, cancellationToken);
            await planner.EnqueueJamAsync(store, job.RunId.Value, cancellationToken);
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
