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
        if (new[] { "fullstats", "funnel", "clusters", "jam" }.All(kind => active.Any(x => x.Kind == kind)))
            return new WbRefreshRun(active[0].RunId ?? Guid.NewGuid(), false, active, [], true);

        var campaignsRefreshed = false;
        if (store.CampaignsRefreshedAtUtc == null || store.CampaignsRefreshedAtUtc < DateTime.UtcNow.AddHours(-1))
        {
            try
            {
                var adverts = await wb.GetCampaignsAsync(tokens.Unprotect(store.ApiKey!), ct);
                await UpsertCampaignsAsync(store, adverts, ct);
                campaignsRefreshed = true;
            }
            catch (WbApiException error)
            {
                logger.LogWarning("WB campaign list refresh returned {StatusCode} for store {StoreId}; using saved list",
                    (int)error.StatusCode, store.Id);
            }
        }

        var runId = active.FirstOrDefault(x => x.Kind == "fullstats")?.RunId ?? Guid.NewGuid();
        var jobs = new List<WbSyncJob>();
        var skipped = new List<WbSkippedSource>();
        async Task Add(string kind, Task<WbPlanResult> task)
        {
            var result = await task;
            if (result.Job != null) jobs.Add(result.Job);
            else skipped.Add(new WbSkippedSource(kind, result.Reason ?? "нет данных"));
        }
        await Add("fullstats", EnqueueFullStatsAsync(store, runId, ct));
        await Add("funnel", EnqueueFunnelAsync(store, runId, ct));
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
        if (ids.Length == 0) return new(null, "нет кампаний");
        var end = Yesterday();
        return new(await QueueAsync(store.Id, "fullstats", end.AddDays(-29), end,
            JsonSerializer.Serialize(ids), null, runId, ct));
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
        var pairs = observed.Where(x => loaded.Contains(x.AdvertId)).ToArray();
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
        var ids = (await ObservedAsync(store.Id, start, end, ct)).Where(x => loaded.Contains(x.AdvertId))
            .Select(x => x.NmId).Distinct().Order().ToArray();
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
            x.Status == "completed" && x.StartDate <= start && x.EndDate >= end)
            .Select(x => x.CampaignIdsJson).ToListAsync(ct);
        return jobs.SelectMany(x => JsonSerializer.Deserialize<long[]>(x) ?? []).ToHashSet();
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
