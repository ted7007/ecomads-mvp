using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Wb;

public sealed class WbSyncWorker(IServiceScopeFactory scopes, ILogger<WbSyncWorker> logger) : BackgroundService
{
    // Basic tokens are conservatively limited to one fullstats request per hour per seller.
    private static readonly TimeSpan FullStatsInterval = TimeSpan.FromHours(1);

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
            job.ErrorCode = "token_missing";
            job.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var ids = JsonSerializer.Deserialize<long[]>(job.CampaignIdsJson) ?? [];
        var pairs = job.Kind == "clusters"
            ? JsonSerializer.Deserialize<WbNormQueryPair[]>(job.PairIdsJson ?? "[]") ?? []
            : [];
        var total = job.Kind == "clusters" ? pairs.Length : ids.Length;
        if (job.NextCampaignOffset >= total)
        {
            Complete(job, store);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var now = DateTime.UtcNow;
        job.Status = "running";
        job.NextAttemptAtUtc = now.Add(FullStatsInterval);
        job.LastRequestAtUtc = now;
        job.UpdatedAtUtc = now;
        // Reserve the slot before the HTTP request. A crash may delay a retry but cannot spend the same slot twice.
        await db.SaveChangesAsync(cancellationToken);

        var wb = scope.ServiceProvider.GetRequiredService<IWbPromotionClient>();
        var tokens = scope.ServiceProvider.GetRequiredService<IWbTokenService>();
        var batch = ids.Skip(job.NextCampaignOffset).Take(50).ToArray();
        var pairBatch = pairs.Skip(job.NextCampaignOffset).Take(100).ToArray();
        try
        {
            var token = tokens.Unprotect(store.ApiKey);
            if (job.Kind == "clusters")
            {
                using var response = await wb.GetNormQueryStatsAsync(token, pairBatch,
                    job.StartDate, job.EndDate, cancellationToken);
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
                var importer = scope.ServiceProvider.GetRequiredService<WbFullStatsImporter>();
                await importer.ImportAsync(store.Id, response.RootElement, cancellationToken);
                job.NextCampaignOffset += batch.Length;
            }
            job.AttemptCount = 0;
            job.ErrorCode = null;
            job.UpdatedAtUtc = DateTime.UtcNow;
            if (job.NextCampaignOffset >= total) Complete(job, store);
            await db.SaveChangesAsync(cancellationToken);
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
            }
            else if (error.RetryAfter.HasValue)
            {
                var retryAt = DateTime.UtcNow.Add(error.RetryAfter.Value);
                if (retryAt > job.NextAttemptAtUtc) job.NextAttemptAtUtc = retryAt;
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception error) when (error is JsonException or HttpRequestException or InvalidOperationException or CryptographicException)
        {
            logger.LogWarning(error, "WB sync step failed for job {JobId}", job.Id);
            job.AttemptCount++;
            job.ErrorCode = error is JsonException ? "invalid_response"
                : error is CryptographicException ? "token_unreadable" : "transport_or_storage";
            job.UpdatedAtUtc = DateTime.UtcNow;
            if (job.AttemptCount >= 3 || error is JsonException or CryptographicException) job.Status = "failed";
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static void Complete(WbSyncJob job, Store store)
    {
        job.Status = "completed";
        job.UpdatedAtUtc = DateTime.UtcNow;
        if (job.Kind == "fullstats") store.LastSyncAt = job.UpdatedAtUtc;
    }
}
