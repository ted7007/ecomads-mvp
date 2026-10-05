using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ecomads.WebApplication.Services.Wb;

public sealed record WbPlanResult(WbSyncJob? Job, string? Reason = null);
public sealed record WbSkippedSource(string Kind, string Reason);
public sealed record WbRefreshRun(Guid RunId, bool CampaignsRefreshed, IReadOnlyList<WbSyncJob> Jobs,
    IReadOnlyList<WbSkippedSource> Skipped, bool AlreadyActive);

public sealed class WbSyncPlanner(EcomadsDbContext db, IWbPromotionClient wb, IWbTokenService tokens,
    ILogger<WbSyncPlanner> logger)
{
    public static DateOnly Yesterday()
    {
        var moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, moscow).DateTime).AddDays(-1);
    }

    public async Task<WbRefreshRun> StartRefreshAsync(Store store, CancellationToken ct)
    {
        var active = await db.WbSyncJobs.Where(x => x.StoreId == store.Id &&
            (x.Status == "pending" || x.Status == "running")).ToListAsync(ct);
        if (active.Any(x => x.Kind == "fullstats") && active.Any(x => x.Kind == "funnel_recent") &&
            active.Any(x => x.Kind == "expenses"))
            return new WbRefreshRun(active[0].RunId ?? Guid.NewGuid(), false, active, [], true);

        var campaignsRefreshed = false;
        if (store.CampaignsRefreshedAtUtc == null || store.CampaignsRefreshedAtUtc < DateTime.UtcNow.AddHours(-1))
        {
            try
            {
                var now = DateTime.UtcNow;
                if (await WbRateLimits.TryReserveAsync(db, store.Id, "campaign_list", 0, now, ct))
                {
                    var adverts = await wb.GetCampaignsAsync(tokens.Unprotect(store.ApiKey!), ct);
                    await UpsertCampaignsAsync(store, adverts, ct);
                    campaignsRefreshed = true;
                }
            }
            catch (WbApiException error)
            {
                if (error.RetryAfter.HasValue)
                    await WbRateLimits.DeferUntilAsync(db, store.Id, "campaign_list", 0,
                        DateTime.UtcNow.Add(error.RetryAfter.Value), ct);
                logger.LogWarning("WB campaign list refresh returned {StatusCode} for store {StoreId}; using saved list",
                    (int)error.StatusCode, store.Id);
            }
        }

        var runId = active.FirstOrDefault(x => x.Kind == "fullstats" || x.Kind == "funnel_recent")?.RunId ?? Guid.NewGuid();
        var jobs = new List<WbSyncJob>();
        var skipped = new List<WbSkippedSource>();
        async Task Add(string kind, Task<WbPlanResult> task)
        {
            var result = await task;
            if (result.Job != null) jobs.Add(result.Job);
            else skipped.Add(new WbSkippedSource(kind, result.Reason ?? "нет данных"));
        }
        await Add("fullstats", EnqueueFullStatsAsync(store, runId, ct));
        await Add("funnel_recent", EnqueueRecentFunnelAsync(store, runId, ct));
        await Add("expenses", EnqueueExpensesAsync(store, runId, ct));
        await Add("archive", EnqueueArchiveAsync(store, runId, ct));
        await Add("funnel_backfill", EnqueueBackfillAsync(store, runId, ct));
        var end = Yesterday();
        var covered = await db.WbSyncJobs.AnyAsync(x => x.StoreId == store.Id && x.Kind == "fullstats" &&
            x.Status == "completed" && x.StartDate <= end.AddDays(-6) && x.EndDate >= end, ct);
        if (covered)
        {
            await Add("clusters", EnqueueClustersAsync(store, runId, ct));
            await Add("jam", EnqueueJamAsync(store, runId, ct));
        }
        return new WbRefreshRun(runId, campaignsRefreshed, jobs, skipped, false);
    }

    public async Task UpsertCampaignsAsync(Store store, IReadOnlyList<WbCampaignInfo> adverts, CancellationToken ct)
    {
        var existing = await db.Campaigns.Where(x => x.StoreId == store.Id).ToDictionaryAsync(x => x.WbCampaignId, ct);
        var now = DateTime.UtcNow;
        foreach (var advert in adverts)
        {
            var wbId = advert.Id.ToString();
            if (!existing.TryGetValue(wbId, out var campaign))
            {
                campaign = new Campaign { Id = Guid.NewGuid(), StoreId = store.Id, WbCampaignId = wbId, CreatedAt = now };
                db.Campaigns.Add(campaign);
            }
            campaign.Name = advert.Name;
            campaign.IsActive = advert.Status == 9;
            campaign.WbStatus = advert.Status;
            campaign.LastSeenAt = now;
            campaign.WbCreatedAtUtc = advert.CreatedAtUtc;
            campaign.WbStartedAtUtc = advert.StartedAtUtc;
            campaign.WbDeletedAtUtc = advert.DeletedAtUtc;
            campaign.WbUpdatedAtUtc = advert.UpdatedAtUtc;
        }
        store.CampaignsRefreshedAtUtc = now;
        await db.SaveChangesAsync(ct);
    }

    public async Task<WbPlanResult> EnqueueFullStatsAsync(Store store, Guid runId, CancellationToken ct)
    {
        var active = await ActiveAsync(store.Id, "fullstats", ct);
        if (active != null) return new(active);
        var ids = (await db.Campaigns.Where(x => x.StoreId == store.Id && (x.WbStatus == 9 || x.WbStatus == 11))
            .OrderBy(x => x.WbStatus == 9 ? 0 : 1).Select(x => x.WbCampaignId).ToListAsync(ct))
            .Select(x => long.TryParse(x, out var id) ? id : 0).Where(x => x > 0).ToArray();
        var archive = await ActiveAsync(store.Id, "archive", ct) == null
            ? await EligibleArchiveIdsAsync(store.Id, Yesterday(), ct) : [];
        ids = ids.Concat(archive.Take(Math.Max(0, 50 - ids.Length))).Distinct().ToArray();
        if (ids.Length == 0) return new(null, "нет кампаний");
        var end = Yesterday();
        return new(await QueueAsync(store.Id, "fullstats", end.AddDays(-30), end,
            JsonSerializer.Serialize(ids), null, runId, ct));
    }

    public async Task<WbPlanResult> EnqueueArchiveAsync(Store store, Guid runId, CancellationToken ct)
    {
        var active = await ActiveAsync(store.Id, "archive", ct);
        if (active != null) return new(active);
        var currentCount = await db.Campaigns.CountAsync(x => x.StoreId == store.Id &&
            (x.WbStatus == 9 || x.WbStatus == 11), ct);
        var ids = (await EligibleArchiveIdsAsync(store.Id, Yesterday(), ct))
            .Skip(Math.Max(0, 50 - currentCount)).ToArray();
        if (ids.Length == 0) return new(null, "архив уже проверен");
        var end = Yesterday();
        return new(await QueueAsync(store.Id, "archive", end.AddDays(-30), end,
            JsonSerializer.Serialize(ids), null, runId, ct));
    }

    private async Task<long[]> EligibleArchiveIdsAsync(Guid storeId, DateOnly end, CancellationToken ct)
    {
        var candidates = await db.Campaigns.Where(x => x.StoreId == storeId && x.WbStatus == 7 &&
                (x.WbCreatedAtUtc == null || x.WbCreatedAtUtc <= DateTime.SpecifyKind(end.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc)))
            .OrderByDescending(x => x.WbUpdatedAtUtc).ToListAsync(ct);
        var ids = new List<long>();
        foreach (var campaign in candidates)
        {
            if (WbCampaignEligibility.FinishedBefore(campaign, end.AddDays(-30))) continue;
            var firstDate = campaign.WbCreatedAtUtc is { } created
                ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(created, TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow")))
                : end.AddDays(-30);
            if (firstDate > end) continue;
            if (firstDate < end.AddDays(-30)) firstDate = end.AddDays(-30);
            var checks = await db.WbCampaignDailyChecks.Where(x => x.CampaignId == campaign.Id &&
                    x.Date >= firstDate && x.Date <= end && x.CheckedAtUtc > DateTime.UtcNow.AddDays(-7) &&
                    x.Result != "unknown")
                .CountAsync(ct);
            if (checks == end.DayNumber - firstDate.DayNumber + 1) continue;
            if (long.TryParse(campaign.WbCampaignId, out var id)) ids.Add(id);
        }
        return ids.ToArray();
    }

    public async Task<WbPlanResult> EnqueueExpensesAsync(Store store, Guid runId, CancellationToken ct)
    {
        var active = await ActiveAsync(store.Id, "expenses", ct);
        if (active != null) return new(active);
        var end = Yesterday();
        return new(await QueueAsync(store.Id, "expenses", end.AddDays(-30), end, "[]", null, runId, ct));
    }

    public async Task<WbPlanResult> EnqueueRecentFunnelAsync(Store store, Guid runId, CancellationToken ct)
    {
        var active = await ActiveAsync(store.Id, "funnel_recent", ct);
        if (active != null) return new(active);
        var end = Yesterday();
        return new(await QueueAsync(store.Id, "funnel_recent", end.AddDays(-6), end, "[]", null, runId, ct));
    }

    public async Task<WbPlanResult> EnqueueBackfillAsync(Store store, Guid runId, CancellationToken ct)
    {
        var active = await ActiveAsync(store.Id, "funnel_backfill", ct);
        if (active != null) return new(active);
        var end = Yesterday();
        var pairIds = await FunnelPairIdsAsync(store.Id, end, ct);
        if (pairIds.Length == 0) return new(null, "история заказов уже загружена");
        return new(await QueueAsync(store.Id, "funnel_backfill", end.AddDays(-29), end.AddDays(-7),
            JsonSerializer.Serialize(pairIds), null, runId, ct));
    }

    public async Task<WbPlanResult> EnqueueFullStatsAsync(Store store, DateOnly start, DateOnly end,
        long[]? selectedIds, CancellationToken ct)
    {
        var active = await ActiveAsync(store.Id, "fullstats", ct);
        if (active != null) return new(active);
        var campaigns = await db.Campaigns.Where(x => x.StoreId == store.Id &&
            (x.WbStatus == 7 || x.WbStatus == 9 || x.WbStatus == 11))
            .OrderBy(x => x.WbStatus == 9 ? 0 : x.WbStatus == 11 ? 1 : 2)
            .Select(x => new { x.WbCampaignId, x.WbStatus }).ToListAsync(ct);
        var available = campaigns.Select(x => long.TryParse(x.WbCampaignId, out var id) ? id : 0)
            .Where(x => x > 0).ToHashSet();
        var requested = selectedIds?.Distinct().ToArray();
        if (requested is { Length: > 0 } && requested.Any(x => !available.Contains(x)))
            return new(null, "Выбрана кампания, недоступная для статистики этого кабинета.");
        var ids = requested is { Length: > 0 } ? requested : campaigns.Where(x => x.WbStatus is 9 or 11)
            .Select(x => long.TryParse(x.WbCampaignId, out var id) ? id : 0).Where(x => x > 0).ToArray();
        if (ids.Length == 0) return new(null, "Нет кампаний WB, для которых доступна статистика.");
        return new(await QueueAsync(store.Id, "fullstats", start, end, JsonSerializer.Serialize(ids), null, null, ct));
    }

    public async Task<WbPlanResult> EnqueueYearFullStatsAsync(Store store, DateOnly start, DateOnly end,
        CancellationToken ct)
    {
        var active = await ActiveAsync(store.Id, "fullstats_history", ct);
        if (active != null) return new(active);
        var previous = await db.WbSyncJobs.Where(x => x.StoreId == store.Id &&
                x.Kind == "fullstats_history" && x.Status == "completed" &&
                x.StartDate == start && x.EndDate == end)
            .OrderByDescending(x => x.CompletedAtUtc).FirstOrDefaultAsync(ct);
        if (previous != null) return new(previous);

        var campaigns = await db.Campaigns.Where(x => x.StoreId == store.Id &&
                (x.WbStatus == 7 || x.WbStatus == 9 || x.WbStatus == 11))
            .ToListAsync(ct);
        var batches = new List<WbHistoryBatch>();
        foreach (var (chunkStart, chunkEnd) in HistoryPeriods(start, end))
        {
            var ids = campaigns.Where(x => !WbCampaignEligibility.FinishedBefore(x, chunkStart) &&
                    !WbCampaignEligibility.CreatedAfter(x, chunkEnd))
                .Select(x => long.TryParse(x.WbCampaignId, out var id) ? id : 0)
                .Where(x => x > 0).ToArray();
            foreach (var group in ids.Chunk(50))
                batches.Add(new WbHistoryBatch(chunkStart, chunkEnd, group));
        }
        if (batches.Count == 0) return new(null, "Нет кампаний за выбранный год.");
        return new(await QueueAsync(store.Id, "fullstats_history", start, end, "[]",
            JsonSerializer.Serialize(batches), null, ct));
    }

    public async Task<WbPlanResult> EnqueueYearExpensesAsync(Store store, DateOnly start, DateOnly end,
        CancellationToken ct)
    {
        var active = await ActiveAsync(store.Id, "expenses_history", ct);
        if (active != null) return new(active);
        var previous = await db.WbSyncJobs.Where(x => x.StoreId == store.Id &&
                x.Kind == "expenses_history" && x.Status == "completed" &&
                x.StartDate == start && x.EndDate == end)
            .OrderByDescending(x => x.CompletedAtUtc).FirstOrDefaultAsync(ct);
        if (previous != null) return new(previous);
        var loaded = (await db.WbStoreDailySpends.Where(x => x.StoreId == store.Id &&
                x.Date >= start && x.Date <= end).Select(x => x.Date).ToListAsync(ct)).ToHashSet();
        var batches = ExpenseHistoryBatches(start, end, loaded);
        if (batches.Length == 0) return new(null, "Расходы за выбранный год уже загружены.");
        return new(await QueueAsync(store.Id, "expenses_history", start, end, "[]",
            JsonSerializer.Serialize(batches), null, ct));
    }

    public async Task<WbPlanResult> EnqueueYearOrdersAsync(Store store, DateOnly start, DateOnly end,
        CancellationToken ct)
    {
        var olderEnd = end.AddDays(-7);
        var active = await ActiveAsync(store.Id, "funnel_year", ct);
        if (active != null) return new(active);
        var previous = await db.WbSyncJobs.Where(x => x.StoreId == store.Id &&
                x.Kind == "funnel_year" && x.Status == "completed" &&
                x.StartDate == start && x.EndDate == olderEnd)
            .OrderByDescending(x => x.CompletedAtUtc).FirstOrDefaultAsync(ct);
        if (previous != null) return new(previous);
        var loaded = (await db.WbStoreDailyOrders.Where(x => x.StoreId == store.Id &&
                x.Date >= start && x.Date <= olderEnd).Select(x => x.Date).ToListAsync(ct)).ToHashSet();
        var firstDays = new List<long>();
        for (var day = start; day <= olderEnd; day = day.AddDays(1))
        {
            if (loaded.Contains(day)) continue;
            firstDays.Add(long.Parse(day.ToString("yyyyMMdd")));
            if (day < olderEnd && !loaded.Contains(day.AddDays(1))) day = day.AddDays(1);
        }
        if (firstDays.Count == 0) return new(null, "Заказы за выбранный год уже загружены.");
        return new(await QueueAsync(store.Id, "funnel_year", start, olderEnd,
            JsonSerializer.Serialize(firstDays), null, null, ct));
    }

    private static IEnumerable<(DateOnly Start, DateOnly End)> HistoryPeriods(DateOnly start, DateOnly end)
    {
        for (var current = start; current <= end; current = current.AddDays(31))
        {
            var chunkEnd = current.AddDays(30);
            yield return (current, chunkEnd < end ? chunkEnd : end);
        }
    }

    public static WbHistoryBatch[] ExpenseHistoryBatches(DateOnly start, DateOnly end,
        IReadOnlySet<DateOnly> loaded)
    {
        var batches = new List<WbHistoryBatch>();
        for (var current = start; current <= end; current = current.AddDays(29))
        {
            var chunkEnd = current.AddDays(28);
            if (chunkEnd > end) chunkEnd = end;
            if (Enumerable.Range(0, chunkEnd.DayNumber - current.DayNumber + 1)
                .Any(offset => !loaded.Contains(current.AddDays(offset))))
                batches.Add(new WbHistoryBatch(current, chunkEnd, []));
        }
        return batches.ToArray();
    }

    public async Task<WbPlanResult> EnqueueFunnelAsync(Store store, Guid runId, CancellationToken ct)
    {
        var active = await ActiveAsync(store.Id, "funnel", ct);
        if (active != null) return new(active);
        var end = Yesterday();
        var pairIds = await FunnelPairIdsAsync(store.Id, end, ct);
        return new(await QueueAsync(store.Id, "funnel", end.AddDays(-6), end,
            JsonSerializer.Serialize(pairIds), null, runId, ct));
    }

    public async Task<WbPlanResult> EnqueueFunnelAsync(Store store, CancellationToken ct)
    {
        var active = await ActiveAsync(store.Id, "funnel", ct);
        if (active != null) return new(active);
        var end = Yesterday();
        var pairIds = await FunnelPairIdsAsync(store.Id, end, ct);
        return new(await QueueAsync(store.Id, "funnel", end.AddDays(-6), end,
            JsonSerializer.Serialize(pairIds), null, null, ct));
    }

    private async Task<long[]> FunnelPairIdsAsync(Guid storeId, DateOnly yesterday, CancellationToken ct)
    {
        var olderStart = yesterday.AddDays(-29);
        var olderEnd = yesterday.AddDays(-7);
        var loaded = (await db.WbStoreDailyOrders.Where(x => x.StoreId == storeId &&
            x.Date >= olderStart && x.Date <= olderEnd).Select(x => x.Date).ToListAsync(ct)).ToHashSet();
        var firstDays = new List<long>();
        for (var day = olderStart; day <= olderEnd; day = day.AddDays(1))
        {
            if (loaded.Contains(day)) continue;
            firstDays.Add(long.Parse(day.ToString("yyyyMMdd")));
            if (day < olderEnd && !loaded.Contains(day.AddDays(1))) day = day.AddDays(1);
        }
        return firstDays.ToArray();
    }

    public async Task<WbPlanResult> EnqueueClustersAsync(Store store, Guid runId, CancellationToken ct)
    {
        var active = await ActiveAsync(store.Id, "clusters", ct);
        if (active != null) return new(active);
        var end = Yesterday();
        var start = end.AddDays(-6);
        var loaded = await LoadedCampaignIdsAsync(store.Id, start, end, ct);
        var observed = await ObservedAsync(store.Id, start, end, ct);
        var already = (await db.WbSyncJobs.Where(x => x.StoreId == store.Id && x.RunId == runId && x.Kind == "clusters")
            .Select(x => x.PairIdsJson).ToListAsync(ct))
            .SelectMany(x => JsonSerializer.Deserialize<WbNormQueryPair[]>(x ?? "[]") ?? []).ToHashSet();
        var pairs = observed.Where(x => loaded.Contains(x.AdvertId) && !already.Contains(x)).ToArray();
        if (pairs.Length == 0) return new(null, "нет данных о товарах");
        return new(await QueueAsync(store.Id, "clusters", start, end, "[]", JsonSerializer.Serialize(pairs), runId, ct));
    }

    public async Task<WbPlanResult> EnqueueClustersAsync(Store store, DateOnly start, DateOnly end,
        long[]? selectedIds, CancellationToken ct)
    {
        var active = await ActiveAsync(store.Id, "clusters", ct);
        if (active != null) return new(active);
        var selected = selectedIds?.Distinct().ToArray();
        if (selected is { Length: > 0 } && !await OwnsCampaignIdsAsync(store.Id, selected, ct))
            return new(null, "Выбрана кампания другого кабинета.");
        var loaded = await LoadedCampaignIdsAsync(store.Id, start, end, ct);
        var pairs = (await ObservedAsync(store.Id, start, end, ct))
            .Where(x => loaded.Contains(x.AdvertId) && (selected is not { Length: > 0 } || selected.Contains(x.AdvertId)))
            .ToArray();
        if (pairs.Length == 0) return new(null, "Сначала загрузите статистику выбранных кампаний за этот период.");
        return new(await QueueAsync(store.Id, "clusters", start, end, "[]", JsonSerializer.Serialize(pairs), null, ct));
    }

    public async Task<WbPlanResult> EnqueueJamAsync(Store store, Guid runId, CancellationToken ct)
    {
        var active = await ActiveAsync(store.Id, "jam", ct);
        if (active != null) return new(active);
        if (store.JamStatus is "payment_required" or "access_denied") return new(null, "нет подписки Джем");
        var end = Yesterday();
        var start = end.AddDays(-6);
        var loaded = await LoadedCampaignIdsAsync(store.Id, start, end, ct);
        var already = (await db.WbSyncJobs.Where(x => x.StoreId == store.Id && x.RunId == runId && x.Kind == "jam")
            .Select(x => x.CampaignIdsJson).ToListAsync(ct))
            .SelectMany(x => JsonSerializer.Deserialize<long[]>(x) ?? []).ToHashSet();
        var ids = (await ObservedAsync(store.Id, start, end, ct)).Where(x => loaded.Contains(x.AdvertId))
            .Select(x => x.NmId).Where(x => !already.Contains(x)).Distinct().Order().ToArray();
        if (ids.Length == 0) return new(null, "нет данных о товарах");
        return new(await QueueAsync(store.Id, "jam", start, end, JsonSerializer.Serialize(ids), null, runId, ct));
    }

    public async Task<WbPlanResult> EnqueueJamAsync(Store store, DateOnly start, DateOnly end,
        long[]? selectedIds, CancellationToken ct)
    {
        var active = await ActiveAsync(store.Id, "jam", ct);
        if (active != null) return new(active);
        var selected = selectedIds?.Distinct().ToArray();
        if (selected is { Length: > 0 } && !await OwnsCampaignIdsAsync(store.Id, selected, ct))
            return new(null, "Выбрана кампания другого кабинета.");
        var ids = (await ObservedAsync(store.Id, start, end, ct))
            .Where(x => selected is not { Length: > 0 } || selected.Contains(x.AdvertId))
            .Select(x => x.NmId).Distinct().Order().ToArray();
        if (ids.Length == 0) return new(null, "Сначала загрузите статистику кампаний за этот период.");
        return new(await QueueAsync(store.Id, "jam", start, end, JsonSerializer.Serialize(ids), null, null, ct));
    }

    private async Task<bool> OwnsCampaignIdsAsync(Guid storeId, long[] selected, CancellationToken ct)
    {
        var ids = await db.Campaigns.Where(x => x.StoreId == storeId).Select(x => x.WbCampaignId).ToListAsync(ct);
        var owned = ids.Where(x => long.TryParse(x, out _)).Select(long.Parse).ToHashSet();
        return selected.All(owned.Contains);
    }

    private async Task<HashSet<long>> LoadedCampaignIdsAsync(Guid storeId, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var jobs = await db.WbSyncJobs.Where(x => x.StoreId == storeId && x.Kind == "fullstats" &&
            (x.Status == "completed" || x.Status == "running") && x.StartDate <= start && x.EndDate >= end)
            .Select(x => new { x.CampaignIdsJson, x.NextCampaignOffset, x.Status }).ToListAsync(ct);
        var candidates = jobs.SelectMany(x => (JsonSerializer.Deserialize<long[]>(x.CampaignIdsJson) ?? [])
            .Take(x.Status == "completed" ? int.MaxValue : x.NextCampaignOffset)).ToHashSet();
        var checkedIds = await (from check in db.WbCampaignDailyChecks
            join campaign in db.Campaigns on check.CampaignId equals campaign.Id
            where campaign.StoreId == storeId && check.Date >= start && check.Date <= end
            select campaign.WbCampaignId).Distinct().ToListAsync(ct);
        return checkedIds.Where(x => long.TryParse(x, out var id) && candidates.Contains(id))
            .Select(long.Parse).ToHashSet();
    }

    private async Task<WbNormQueryPair[]> ObservedAsync(Guid storeId, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var fromUtc = DateTime.SpecifyKind(start.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(end.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var observed = await (from stat in db.CampaignNomenclatureStatistics
            join campaign in db.Campaigns on stat.CampaignId equals campaign.Id
            join article in db.Nomenclatures on stat.NomenclatureId equals article.Id
            where campaign.StoreId == storeId && stat.Date >= fromUtc && stat.Date <= toUtc
            select new { campaign.WbCampaignId, article.WbNomenclatureId }).Distinct().ToListAsync(ct);
        return observed.Where(x => long.TryParse(x.WbCampaignId, out _) && long.TryParse(x.WbNomenclatureId, out _))
            .Select(x => new WbNormQueryPair(long.Parse(x.WbCampaignId), long.Parse(x.WbNomenclatureId))).ToArray();
    }

    private Task<WbSyncJob?> ActiveAsync(Guid storeId, string kind, CancellationToken ct) =>
        db.WbSyncJobs.FirstOrDefaultAsync(x => x.StoreId == storeId && x.Kind == kind &&
            (x.Status == "pending" || x.Status == "running"), ct);

    private async Task<WbSyncJob> QueueAsync(Guid storeId, string kind, DateOnly start, DateOnly end,
        string ids, string? pairs, Guid? runId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var next = await WbRateLimits.NextSlotAsync(db, storeId, kind, now, ct);
        var job = new WbSyncJob
        {
            Id = Guid.NewGuid(), StoreId = storeId, RunId = runId, Kind = kind, StartDate = start, EndDate = end,
            CampaignIdsJson = ids, PairIdsJson = pairs, Status = "pending",
            CreatedAtUtc = now, UpdatedAtUtc = now, NextAttemptAtUtc = next,
            Stage = next > now.AddSeconds(5) ? "waiting" : "queued",
            WaitReason = next > now.AddSeconds(5) ? "rate_limit" : null
        };
        var queuedEvent = new WbSyncJobEvent { JobId = job.Id, OccurredAtUtc = now, Stage = job.Stage };
        db.WbSyncJobs.Add(job);
        db.WbSyncJobEvents.Add(queuedEvent);
        try
        {
            await db.SaveChangesAsync(ct);
            return job;
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: "23505" })
        {
            db.Entry(queuedEvent).State = EntityState.Detached;
            db.Entry(job).State = EntityState.Detached;
            var existing = await ActiveAsync(storeId, kind, ct);
            if (existing != null) return existing;
            throw;
        }
    }
}
