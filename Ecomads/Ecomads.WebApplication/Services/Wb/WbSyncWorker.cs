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

    public async Task ProcessNextAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EcomadsDbContext>();
        var job = await db.WbSyncJobs
            .Where(x => WbSyncJobUnits.SupportedKinds.Contains(x.Kind) && (x.Status == "pending" || x.Status == "running") && x.PauseRequestedAtUtc == null &&
                x.NextAttemptAtUtc <= DateTime.UtcNow && db.Stores.Any(s => s.Id == x.StoreId &&
                    !s.AutoRefreshBlocked && (s.TokenExpiresAtUtc == null || s.TokenExpiresAtUtc > DateTime.UtcNow)))
            .OrderBy(x => x.Kind == "fullstats" || x.Kind == "expenses" || x.Kind == "funnel_recent" ? 0 :
                x.Kind == "fullstats_month" || x.Kind == "expenses_month" || x.Kind == "funnel_month" || x.Kind == "archive" || x.Kind == "funnel_backfill" ? 1 :
                x.Kind == "clusters" || x.Kind == "jam" ? 2 : 3)
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
        if (job.Kind is "fullstats" or "fullstats_month" or "archive")
        {
            // Drain jobs queued by the old immediate-retry policy without requesting the same IDs again.
            var processed = ids.Take(job.NextCampaignOffset).ToHashSet();
            ids = ids.Take(job.NextCampaignOffset)
                .Concat(ids.Skip(job.NextCampaignOffset).Where(x => !processed.Contains(x)).Distinct()).ToArray();
            job.CampaignIdsJson = JsonSerializer.Serialize(ids);
            job.PairIdsJson = null;
        }
        var history = job.Kind is "fullstats_history" or "expenses_history" or "expenses_month"
            ? JsonSerializer.Deserialize<WbHistoryBatch[]>(job.PairIdsJson ?? "[]") ?? []
            : [];
        if (job.Kind == "expenses_history" && history.Any(x =>
                x.EndDate.DayNumber - x.StartDate.DayNumber > 28))
        {
            var loaded = (await db.WbStoreDailySpends.Where(x => x.StoreId == job.StoreId &&
                    x.Date >= job.StartDate && x.Date <= job.EndDate)
                .Select(x => x.Date).ToListAsync(cancellationToken)).ToHashSet();
            history = WbSyncPlanner.ExpenseHistoryBatches(job.StartDate, job.EndDate, loaded);
            job.PairIdsJson = JsonSerializer.Serialize(history);
            job.NextCampaignOffset = 0;
            job.AttemptCount = 0;
            job.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        if (job.Kind == "fullstats_history" && job.NextCampaignOffset < history.Length)
        {
            var campaigns = await db.Campaigns.Where(x => x.StoreId == job.StoreId).ToListAsync(cancellationToken);
            var byId = campaigns.Where(x => long.TryParse(x.WbCampaignId, out _))
                .ToDictionary(x => long.Parse(x.WbCampaignId));
            var revised = history.Take(job.NextCampaignOffset).Concat(history.Skip(job.NextCampaignOffset)
                .Select(x => x with { CampaignIds = x.CampaignIds.Where(id =>
                    !byId.TryGetValue(id, out var campaign) ||
                    !WbCampaignEligibility.FinishedBefore(campaign, x.StartDate)).ToArray() })
                .Where(x => x.CampaignIds.Length > 0)).ToArray();
            if (revised.Length != history.Length || revised.Where((x, i) =>
                    x.CampaignIds.Length != history[i].CampaignIds.Length).Any())
            {
                history = revised;
                job.PairIdsJson = JsonSerializer.Serialize(history);
                job.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        if (job.Kind is "fullstats" or "archive" && job.NextCampaignOffset < ids.Length)
        {
            var remaining = ids.Skip(job.NextCampaignOffset).ToHashSet();
            var campaigns = await db.Campaigns.Where(x => x.StoreId == job.StoreId)
                .ToListAsync(cancellationToken);
            var finished = campaigns.Where(x => long.TryParse(x.WbCampaignId, out var id) &&
                    remaining.Contains(id) && WbCampaignEligibility.FinishedBefore(x, job.StartDate))
                .Select(x => long.Parse(x.WbCampaignId)).ToHashSet();
            if (finished.Count > 0)
            {
                ids = ids.Take(job.NextCampaignOffset)
                    .Concat(ids.Skip(job.NextCampaignOffset).Where(x => !finished.Contains(x))).ToArray();
                job.CampaignIdsJson = JsonSerializer.Serialize(ids);
                if (job.PairIdsJson != null)
                {
                    var retryIds = JsonSerializer.Deserialize<long[]>(job.PairIdsJson) ?? [];
                    job.PairIdsJson = JsonSerializer.Serialize(retryIds.Where(x => !finished.Contains(x)));
                }
                job.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
        }
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

        // A year job never claims the same WB method slot while fresh data still needs it.
        var urgentKinds = job.Kind switch
        {
            "fullstats_history" => new[] { "fullstats", "fullstats_month", "archive" },
            "fullstats_month" => ["fullstats"],
            "expenses_history" => ["expenses", "expenses_month"],
            "expenses_month" => ["expenses"],
            "funnel_year" => ["funnel_recent", "funnel_backfill", "funnel_month"],
            _ => Array.Empty<string>()
        };
        if (urgentKinds.Length > 0 && await db.WbSyncJobs.AsNoTracking().AnyAsync(x =>
                x.StoreId == job.StoreId && x.Id != job.Id && x.PauseRequestedAtUtc == null &&
                (x.Status == "pending" || x.Status == "running") && urgentKinds.Contains(x.Kind),
                cancellationToken))
        {
            job.NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(15);
            job.Stage = "waiting";
            job.WaitReason = "priority";
            job.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (await db.WbSyncJobs.AsNoTracking().AnyAsync(x => x.Id == job.Id &&
                x.PauseRequestedAtUtc != null, cancellationToken)) return;

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
            if (job.Kind is "expenses_history" or "expenses_month")
            {
                var period = history[job.NextCampaignOffset];
                var requestStart = period.StartDate.AddDays(-1);
                var requestEnd = period.EndDate.AddDays(1);
                var yesterday = WbSyncPlanner.Yesterday();
                if (requestEnd > yesterday) requestEnd = yesterday;
                using var response = await wb.GetCostsAsync(token, requestStart, requestEnd, cancellationToken);
                job.Stage = "importing";
                var importer = scope.ServiceProvider.GetRequiredService<WbCostsImporter>();
                job.ImportedRows += await importer.ImportAsync(store.Id, period.StartDate, period.EndDate,
                    response.RootElement, cancellationToken, allowAdjacentDays: true, fillMissingOnly: job.Kind == "expenses_history");
                job.NextCampaignOffset++;
            }
            else if (job.Kind == "fullstats_history")
            {
                var period = history[job.NextCampaignOffset];
                using var response = await wb.GetFullStatsAsync(token, period.CampaignIds,
                    period.StartDate, period.EndDate, cancellationToken);
                if (response.RootElement.ValueKind == JsonValueKind.Null)
                    throw new JsonException("WB вернул null вместо массива статистики.");
                job.Stage = "importing";
                var importer = scope.ServiceProvider.GetRequiredService<WbFullStatsImporter>();
                var imported = await importer.ImportAsync(store.Id, response.RootElement, cancellationToken,
                    period.CampaignIds, job.Id, fillMissingOnly: true,
                    requestedStart: period.StartDate, requestedEnd: period.EndDate);
                job.ImportedRows += imported.Rows;
                job.ItemsWithData += period.CampaignIds.Length - imported.MissingCampaignIds.Length;
                job.ItemsWithoutData = period.Retry
                    ? Math.Max(0, job.ItemsWithoutData - period.CampaignIds.Length + imported.MissingCampaignIds.Length)
                    : job.ItemsWithoutData + imported.MissingCampaignIds.Length;
                job.NextCampaignOffset++;
            }
            else if (job.Kind == "expenses")
            {
                using var response = await wb.GetCostsAsync(token, job.StartDate, job.EndDate, cancellationToken);
                job.Stage = "importing";
                job.UpdatedAtUtc = DateTime.UtcNow;
                Record(db, job);
                await db.SaveChangesAsync(cancellationToken);
                var importer = scope.ServiceProvider.GetRequiredService<WbCostsImporter>();
                job.ImportedRows += await importer.ImportAsync(store.Id, job.StartDate, job.EndDate,
                    // WB can include adjacent Moscow dates in an otherwise valid costs response.
                    // Apply the same bounded filtering as monthly/history imports, keeping only requested days.
                    response.RootElement, cancellationToken, allowAdjacentDays: true);
                job.NextCampaignOffset = 1;
            }
            else if (job.Kind is "funnel_recent" or "funnel_backfill" or "funnel_year" or "funnel_month")
            {
                var funnel = scope.ServiceProvider.GetRequiredService<IWbSalesFunnelClient>();
                var importer = scope.ServiceProvider.GetRequiredService<WbSalesFunnelImporter>();
                if (job.Kind == "funnel_recent")
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
                    var pairIndex = job.NextCampaignOffset;
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
                            page.State, cancellationToken, replaceExisting: job.Kind == "funnel_month",
                            snapshotAtUtc: job.StartedAtUtc);
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
                if (response.RootElement.ValueKind == JsonValueKind.Null)
                    throw new JsonException("WB вернул null вместо массива статистики.");
                job.Stage = "importing";
                job.UpdatedAtUtc = DateTime.UtcNow;
                Record(db, job);
                await db.SaveChangesAsync(cancellationToken);
                var importer = scope.ServiceProvider.GetRequiredService<WbFullStatsImporter>();
                var imported = await importer.ImportAsync(store.Id, response.RootElement, cancellationToken,
                    batch, job.Id, requestedStart: job.StartDate, requestedEnd: job.EndDate);
                job.ImportedRows += imported.Rows;
                job.ItemsWithData += batch.Length - imported.MissingCampaignIds.Length;
                job.ItemsWithoutData += imported.MissingCampaignIds.Length;
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
            if (error.StatusCode == HttpStatusCode.Unauthorized) store.AutoRefreshBlocked = true;
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
            if (error is CryptographicException) store.AutoRefreshBlocked = true;
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
        if (job.Kind is "fullstats" or "fullstats_month") store.LastSyncAt = job.UpdatedAtUtc;
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
