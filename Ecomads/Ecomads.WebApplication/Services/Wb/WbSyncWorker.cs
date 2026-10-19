using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Wb;

public sealed class WbSyncWorker(IServiceScopeFactory scopes, ILogger<WbSyncWorker> logger, TimeProvider? clock = null,
    WbIngestionRollout? rollout = null) : BackgroundService
{
    private DateTime UtcNow => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
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
        var enabledStores = rollout?.StoreIds;
        var job = await db.WbSyncJobs
            .Where(x => (enabledStores == null || enabledStores.Contains(x.StoreId)) && WbSyncJobUnits.SupportedKinds.Contains(x.Kind) && (x.Status == "pending" || x.Status == "running") && x.PauseRequestedAtUtc == null &&
                x.NextAttemptAtUtc <= UtcNow && db.Stores.Any(s => s.Id == x.StoreId &&
                    !s.AutoRefreshBlocked && (s.TokenExpiresAtUtc == null || s.TokenExpiresAtUtc > UtcNow)))
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
            job.UpdatedAtUtc = UtcNow;
            job.CompletedAtUtc = job.UpdatedAtUtc;
            Record(db, job);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var ids = JsonSerializer.Deserialize<long[]>(job.CampaignIdsJson) ?? [];
        if (job.Kind is "funnel_backfill" or "funnel_month" or "funnel_year" && job.PairIdsJson != null)
        {
            var page = JsonSerializer.Deserialize<WbFunnelPageState>(job.PairIdsJson);
            if (page is { Offset: > 0, SeenProductIds: null })
            {
                job.PairIdsJson = null;
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        if (job.Kind is "fullstats" or "fullstats_month" or "archive")
        {
            // Drain jobs queued by the old immediate-retry policy without requesting the same IDs again.
            var processed = ids.Take(job.NextCampaignOffset).ToHashSet();
            ids = ids.Take(job.NextCampaignOffset)
                .Concat(ids.Skip(job.NextCampaignOffset).Where(x => !processed.Contains(x)).Distinct()).ToArray();
            job.CampaignIdsJson = JsonSerializer.Serialize(ids);
            job.PairIdsJson = null;
        }
        var history = job.Kind is "fullstats_history" or "expenses_history" or "expenses_month" or "expenses"
            ? JsonSerializer.Deserialize<WbHistoryBatch[]>(job.PairIdsJson ?? "[]") ?? []
            : [];
        if (job.Kind is "expenses" or "expenses_month" && history.Length == 0)
        {
            history = WbSyncPlanner.ExpenseHistoryBatches(job.StartDate, job.EndDate, new HashSet<DateOnly>());
            job.PairIdsJson = JsonSerializer.Serialize(history);
            await db.SaveChangesAsync(cancellationToken);
        }
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
            job.UpdatedAtUtc = UtcNow;
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
                job.UpdatedAtUtc = UtcNow;
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
                job.UpdatedAtUtc = UtcNow;
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
            job.NextAttemptAtUtc = UtcNow.AddMinutes(15);
            job.Stage = "waiting";
            job.WaitReason = "priority";
            job.UpdatedAtUtc = UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (await db.WbSyncJobs.AsNoTracking().AnyAsync(x => x.Id == job.Id &&
                x.PauseRequestedAtUtc != null, cancellationToken)) return;

        var now = UtcNow;
        // Resume publication after a process/database failure without consuming another WB slot.
        var recoverable = await db.WbResponseSnapshots.AsNoTracking().Where(x => x.JobId == job.Id &&
                x.Page == job.NextCampaignOffset && x.State == "received" && x.AppliedAtUtc == null &&
                x.HttpStatus >= 200 && x.HttpStatus < 300)
            .OrderByDescending(x => x.RequestedAtUtc).ToArrayAsync(cancellationToken);
        foreach (var saved in recoverable)
        {
            var original = JsonSerializer.Deserialize<WbSyncJob>(saved.JobStateJson);
            if (original == null || !WbJobState.SameJson(original.PairIdsJson, job.PairIdsJson) ||
                !WbJobState.SameJson(original.CampaignIdsJson, job.CampaignIdsJson)) continue;
            try
            {
                await new WbResponseProcessor(db, scope.ServiceProvider, clock ?? TimeProvider.System)
                    .ApplyAsync(saved.Id, true, cancellationToken);
                job = await db.WbSyncJobs.SingleAsync(x => x.Id == job.Id, cancellationToken);
                store = await db.Stores.SingleAsync(x => x.Id == job.StoreId, cancellationToken);
                if (job.Kind == "fullstats" || (job.Status == "completed" && job.Kind is "clusters" or "jam"))
                    await PlanFollowupsAsync(scope.ServiceProvider, job, store, cancellationToken);
                return;
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException or KeyNotFoundException)
            {
                using var failedContext = new WbIngestionContext(original, saved.RequestedAtUtc) {
                    SnapshotId = saved.Id, ReceivedAtUtc = saved.ReceivedAtUtc };
                await MarkResponseErrorAsync(db, failedContext, original, cancellationToken,
                    (error as WbResponseImportException)?.ReasonCode);
                job = await db.WbSyncJobs.SingleAsync(x => x.Id == job.Id, cancellationToken);
                store = await db.Stores.SingleAsync(x => x.Id == job.StoreId, cancellationToken);
            }
        }
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
        using var capture = new WbIngestionContext(job, now);
        try
        {
            var token = tokens.Unprotect(store.ApiKey ?? throw new CryptographicException("Токен кабинета отсутствует."));
            using var response = await FetchAsync(scope.ServiceProvider, job, token, cancellationToken);
            if (capture.SnapshotId == null)
            {
                // Test doubles/custom clients still use the same durable application path.
                var raw = response.RootElement.GetRawText();
                var snapshot = new WbResponseSnapshot { Id = Guid.NewGuid(), StoreId = job.StoreId, JobId = job.Id,
                    Kind = job.Kind, JobStateJson = JsonSerializer.Serialize(job), Page = job.NextCampaignOffset,
                    RequestedAtUtc = now, ReceivedAtUtc = UtcNow, HttpStatus = 200, Body = raw,
                    Sha256 = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))) };
                db.WbResponseSnapshots.Add(snapshot);
                await db.SaveChangesAsync(cancellationToken);
                capture.SnapshotId = snapshot.Id;
            }
            var processor = new WbResponseProcessor(db, scope.ServiceProvider, clock ?? TimeProvider.System);
            await processor.ApplyAsync(capture.SnapshotId.Value, true, cancellationToken);
            job = await db.WbSyncJobs.SingleAsync(x => x.Id == job.Id, cancellationToken);
            store = await db.Stores.SingleAsync(x => x.Id == job.StoreId, cancellationToken);
            if (job.Kind == "fullstats" || (job.Status == "completed" && job.Kind is "clusters" or "jam"))
                await PlanFollowupsAsync(scope.ServiceProvider, job, store, cancellationToken);
        }
        catch (WbApiException error)
        {
            db.ChangeTracker.Clear();
            await MarkResponseErrorAsync(db, capture, job, cancellationToken);
            job = await db.WbSyncJobs.SingleAsync(x => x.Id == job.Id, cancellationToken);
            store = await db.Stores.SingleAsync(x => x.Id == job.StoreId, cancellationToken);
            if (job.Kind == "jam" && error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.PaymentRequired)
            {
                store.JamStatus = error.StatusCode == HttpStatusCode.PaymentRequired ? "payment_required" : "access_denied";
                store.JamCheckedAtUtc = UtcNow;
            }
            if (error.StatusCode == HttpStatusCode.Unauthorized) store.AutoRefreshBlocked = true;
            // A quota response means "wait", not that the payload is broken.
            if (error.StatusCode != HttpStatusCode.TooManyRequests) job.AttemptCount++;
            job.ErrorCode = $"wb_{(int)error.StatusCode}";
            job.UpdatedAtUtc = UtcNow;
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
                    var retryAt = UtcNow.Add(error.RetryAfter.Value);
                    if (retryAt > job.NextAttemptAtUtc) job.NextAttemptAtUtc = retryAt;
                    await WbRateLimits.DeferUntilAsync(db, store.Id, job.Kind,
                        job.NextCampaignOffset, retryAt, cancellationToken);
                }
            }
            Record(db, job);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception error) when (error is JsonException or HttpRequestException or IOException or InvalidOperationException or CryptographicException or KeyNotFoundException or FormatException or OverflowException or OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            db.ChangeTracker.Clear();
            await MarkResponseErrorAsync(db, capture, job, cancellationToken, (error as WbResponseImportException)?.ReasonCode);
            job = await db.WbSyncJobs.SingleAsync(x => x.Id == job.Id, cancellationToken);
            store = await db.Stores.SingleAsync(x => x.Id == job.StoreId, cancellationToken);
            logger.LogWarning(error, "WB sync step failed for job {JobId}", job.Id);
            if (error is CryptographicException) store.AutoRefreshBlocked = true;
            job.AttemptCount++;
            job.ErrorCode = error is JsonException or FormatException or OverflowException or KeyNotFoundException ? "invalid_response"
                : error is CryptographicException ? "token_unreadable"
                : error is InvalidOperationException ? "internal_error" : "transport_error";
            job.UpdatedAtUtc = UtcNow;
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

    private async Task<JsonDocument> FetchAsync(IServiceProvider services, WbSyncJob job, string token, CancellationToken ct)
    {
        var wb = services.GetRequiredService<IWbPromotionClient>();
        var ids = JsonSerializer.Deserialize<long[]>(job.CampaignIdsJson) ?? [];
        var offset = job.NextCampaignOffset;
        if (job.Kind is "expenses" or "expenses_month" or "expenses_history")
        {
            var period = JsonSerializer.Deserialize<WbHistoryBatch[]>(job.PairIdsJson!)![offset];
            var window = WbExpenseWindow.Create(period.StartDate, period.EndDate, UtcNow);
            return await wb.GetCostsAsync(token, window.RequestStart, window.RequestEnd, ct);
        }
        if (job.Kind == "fullstats_history")
        {
            var period = JsonSerializer.Deserialize<WbHistoryBatch[]>(job.PairIdsJson!)![offset];
            return await wb.GetFullStatsAsync(token, period.CampaignIds, period.StartDate, period.EndDate, ct);
        }
        if (job.Kind == "funnel_recent") return await services.GetRequiredService<IWbSalesFunnelClient>()
            .GetGroupedHistoryAsync(token, job.StartDate, job.EndDate, ct);
        if (job.Kind is "funnel_year" or "funnel_month" or "funnel_backfill")
        {
            var day = DateOnly.ParseExact(ids[offset].ToString(), "yyyyMMdd");
            var page = JsonSerializer.Deserialize<WbFunnelPageState>(job.PairIdsJson ?? "null") ?? new();
            return await services.GetRequiredService<IWbSalesFunnelClient>().GetProductsAsync(token, day.AddDays(1), day, page.Offset, ct);
        }
        if (job.Kind == "clusters") return await wb.GetNormQueryStatsAsync(token,
            JsonSerializer.Deserialize<WbNormQueryPair[]>(job.PairIdsJson!)!.Skip(offset).Take(100).ToArray(), job.StartDate, job.EndDate, ct);
        if (job.Kind == "jam") return await services.GetRequiredService<IWbJamClient>()
            .GetSearchTextsAsync(token, ids.Skip(offset).Take(50).ToArray(), job.StartDate, job.EndDate, ct);
        return await wb.GetFullStatsAsync(token, ids.Skip(offset).Take(50).ToArray(), job.StartDate, job.EndDate, ct);
    }

    private static async Task MarkResponseErrorAsync(EcomadsDbContext db, WbIngestionContext context, WbSyncJob job, CancellationToken ct,
        string? reason = null)
    {
        if (context.SnapshotId == null) return;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"wb-ingest:" + job.StoreId}, 0))", ct);
            var snapshot = await db.WbResponseSnapshots.FindAsync([context.SnapshotId.Value], ct);
            if (snapshot != null) { snapshot.State = "error"; snapshot.Reason = reason ?? snapshot.Reason ?? "invalid_response"; }
            var source = job.Kind.StartsWith("expenses") ? "expenses" : job.Kind.StartsWith("funnel") ? "orders" :
                job.Kind.StartsWith("fullstats") || job.Kind == "archive" ? "fullstats" : job.Kind;
            var quality = new WbQuality(db);
            var start = job.StartDate; var end = job.EndDate;
            if (job.Kind is "expenses" or "expenses_month" or "expenses_history" or "fullstats_history")
            {
                var periods = JsonSerializer.Deserialize<WbHistoryBatch[]>(job.PairIdsJson ?? "[]") ?? [];
                if (job.NextCampaignOffset < periods.Length)
                { start = periods[job.NextCampaignOffset].StartDate; end = periods[job.NextCampaignOffset].EndDate; }
            }
            if (job.Kind is "funnel_backfill" or "funnel_month" or "funnel_year")
            {
                var ids = JsonSerializer.Deserialize<long[]>(job.CampaignIdsJson) ?? [];
                if (job.NextCampaignOffset < ids.Length)
                { start = DateOnly.ParseExact(ids[job.NextCampaignOffset].ToString(), "yyyyMMdd"); end = start.AddDays(1); }
            }
            var entities = new[] { "" };
            if (source == "fullstats")
            {
                var ids = (JsonSerializer.Deserialize<long[]>(job.CampaignIdsJson) ?? []).Skip(job.NextCampaignOffset).Take(50).ToArray();
                if (job.Kind == "fullstats_history") ids = JsonSerializer.Deserialize<WbHistoryBatch[]>(job.PairIdsJson!)![job.NextCampaignOffset].CampaignIds;
                var wbIds = ids.Select(x => x.ToString()).ToArray();
                entities = (await db.Campaigns.Where(x => x.StoreId == job.StoreId && wbIds.Contains(x.WbCampaignId))
                    .Select(x => x.Id).ToArrayAsync(ct)).Select(x => x.ToString()).ToArray();
            }
            else if (source == "jam")
            {
                var wbIds = (JsonSerializer.Deserialize<long[]>(job.CampaignIdsJson) ?? []).Skip(job.NextCampaignOffset).Take(50)
                    .Select(x => x.ToString()).ToArray();
                entities = (await db.Nomenclatures.Where(x => x.StoreId == job.StoreId && wbIds.Contains(x.WbNomenclatureId))
                    .Select(x => x.Id).ToArrayAsync(ct)).Select(x => x.ToString()).ToArray();
            }
            else if (source == "clusters")
            {
                var pairs = (JsonSerializer.Deserialize<WbNormQueryPair[]>(job.PairIdsJson ?? "[]") ?? []).Skip(job.NextCampaignOffset).Take(100).ToArray();
                var campaigns = await db.Campaigns.Where(x => x.StoreId == job.StoreId).ToDictionaryAsync(x => x.WbCampaignId, ct);
                var articles = await db.Nomenclatures.Where(x => x.StoreId == job.StoreId).ToDictionaryAsync(x => x.WbNomenclatureId, ct);
                entities = pairs.Where(x => campaigns.ContainsKey(x.AdvertId.ToString()) && articles.ContainsKey(x.NmId.ToString()))
                    .Select(x => $"{campaigns[x.AdvertId.ToString()].Id}:{articles[x.NmId.ToString()].Id}").ToArray();
            }
            foreach (var entity in entities)
            for (var date = start; date <= end; date = date.AddDays(1))
            {
                decimal? previous = source == "expenses" ? await db.WbStoreDailySpends.Where(x => x.StoreId == job.StoreId && x.Date == date)
                    .Select(x => (decimal?)x.Spend).SingleOrDefaultAsync(ct) : source == "orders" ?
                    await db.WbStoreDailyOrders.Where(x => x.StoreId == job.StoreId && x.Date == date)
                        .Select(x => (decimal?)x.OrderSum).SingleOrDefaultAsync(ct) : null;
                if (job.Kind == "expenses_history" && previous.HasValue) continue;
                if (job.Kind == "fullstats_history" && Guid.TryParse(entity, out var campaignId))
                {
                    var midnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                    if (await db.WbCampaignDailyChecks.AnyAsync(x => x.CampaignId == campaignId && x.Date == date &&
                            (x.Result == "data" || x.Result == "zero"), ct) &&
                        !await db.CampaignNomenclatureStatistics.AnyAsync(x => x.CampaignId == campaignId && x.Date == midnight &&
                            x.RecommendationFieldsVerified != true, ct)) continue;
                }
                await quality.ObserveAsync(job.StoreId, source, entity, date, source == "jam" ? end : date, "error", ct, snapshot?.Reason, previous);
                if (source == "jam") break;
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    private void Complete(WbSyncJob job, Store store)
    {
        job.Status = "completed";
        job.Stage = "completed";
        job.WaitReason = null;
        job.UpdatedAtUtc = UtcNow;
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
