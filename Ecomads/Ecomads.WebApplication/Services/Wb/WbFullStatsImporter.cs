using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ecomads.WebApplication.Services.Recommendations;

namespace Ecomads.WebApplication.Services.Wb;

public sealed record WbFullStatsImportResult(int Rows, long[] MissingCampaignIds);

public sealed class WbFullStatsImporter(EcomadsDbContext db, IOptions<RecommendationOptions>? recommendations = null)
{
    public async Task<WbFullStatsImportResult> ImportAsync(Guid storeId, JsonElement root, CancellationToken cancellationToken,
        IReadOnlyList<long>? requestedIds = null, Guid? jobId = null, bool fillMissingOnly = false,
        DateOnly? requestedStart = null, DateOnly? requestedEnd = null)
    {
        if (root.ValueKind == JsonValueKind.Null)
            return new WbFullStatsImportResult(0, requestedIds?.ToArray() ?? []);
        if (root.ValueKind != JsonValueKind.Array)
            throw new JsonException("Статистика WB имеет неожиданный формат.");
        if (requestedStart.HasValue != requestedEnd.HasValue || requestedEnd < requestedStart)
            throw new ArgumentException("Необходимо указать корректный период запроса.");

        var campaigns = await db.Campaigns.Where(c => c.StoreId == storeId)
            .ToDictionaryAsync(c => c.WbCampaignId, cancellationToken);
        var nomenclatures = await db.Nomenclatures.Where(n => n.StoreId == storeId)
            .ToDictionaryAsync(n => n.WbNomenclatureId, cancellationToken);

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = db.Database.CurrentTransaction == null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
            var seenCampaigns = new HashSet<long>();
            var acceptedCampaigns = new HashSet<long>();
            var seenDays = new HashSet<(Guid CampaignId, DateOnly Date)>();
            var observedAt = WbIngestionContext.Current?.RequestedAtUtc ?? DateTime.UtcNow;
            var quality = new WbQuality(db);
            var rows = 0;
            // Validate the complete day before deleting any previously accepted rows.
            // Duplicate days invalidate both occurrences, regardless of response order.
            var invalid = new Dictionary<(long Id, DateOnly Date), string>();
            var candidates = new List<(long Id, DateOnly Date, JsonElement Day)>();
            var unknownDays = new HashSet<long>();
            var advertOrdinal = 0;
            foreach (var advert in root.EnumerateArray())
            {
                var originalOrdinal = advertOrdinal++;
                if (advert.ValueKind != JsonValueKind.Object || !advert.TryGetProperty("advertId", out var idElement) ||
                    !idElement.TryGetInt64(out var id) || !campaigns.ContainsKey(id.ToString()) ||
                    requestedIds != null && !requestedIds.Contains(id))
                {
                    throw new JsonException($"fullstats[{originalOrdinal}]: unexpected_or_invalid_campaign");
                }
                seenCampaigns.Add(id);
                if (!advert.TryGetProperty("days", out var days) || days.ValueKind != JsonValueKind.Array)
                { unknownDays.Add(id); continue; }
                foreach (var day in days.EnumerateArray())
                {
                    DateOnly parsed;
                    try { parsed = WbDates.CalendarDay(day.GetProperty("date").GetString()); }
                    catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException)
                    { unknownDays.Add(id); continue; }
                    if (requestedStart.HasValue && (parsed < requestedStart.Value || parsed > requestedEnd!.Value))
                    { WbIngestionContext.Current?.Reject($"fullstats[{originalOrdinal}]: unexpected_date {parsed:yyyy-MM-dd}"); continue; }
                    if (candidates.Any(x => x.Id == id && x.Date == parsed))
                        invalid[(id, parsed)] = "WB вернул повторный день кампании.";
                    candidates.Add((id, parsed, day));
                    try { ValidateDay(day); }
                    catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
                    { invalid[(id, parsed)] = "В дне кампании отсутствуют обязательные числовые поля либо они некорректны."; }
                }
            }
            foreach (var id in unknownDays)
            {
                if (requestedStart.HasValue)
                    for (var date = requestedStart.Value; date <= requestedEnd!.Value; date = date.AddDays(1))
                        invalid[(id, date)] = "В ответе кампании есть строка с неопределённым отчётным днём.";
                else foreach (var candidate in candidates.Where(x => x.Id == id)) invalid[(id, candidate.Date)] = "Неопределённый день кампании.";
            }
            foreach (var issue in invalid)
            {
                var campaign = campaigns[issue.Key.Id.ToString()];
                WbIngestionContext.Current?.Reject($"fullstats campaign {issue.Key.Id}, day {issue.Key.Date:yyyy-MM-dd}: {issue.Value}");
                seenDays.Add((campaign.Id, issue.Key.Date));
                await quality.ObserveAsync(storeId, "fullstats", campaign.Id.ToString(), issue.Key.Date, issue.Key.Date,
                    "ambiguous", cancellationToken, issue.Value);
            }
            using var validated = JsonSerializer.SerializeToDocument(candidates.Where(x => !invalid.ContainsKey((x.Id, x.Date)))
                .GroupBy(x => x.Id).Select(g => new { advertId = g.Key, days = g.Select(x => x.Day).ToArray() }));
            foreach (var advert in validated.RootElement.EnumerateArray())
            {
            var wbNumber = advert.GetProperty("advertId").GetInt64();
            if (requestedIds != null && !requestedIds.Contains(wbNumber))
                throw new JsonException("WB вернул незапрошенную кампанию в fullstats.");
            seenCampaigns.Add(wbNumber);
            var wbId = wbNumber.ToString();
            if (!campaigns.TryGetValue(wbId, out var campaign))
            {
                throw new JsonException("WB вернул неизвестную кампанию в fullstats.");
            }

            foreach (var day in advert.GetProperty("days").EnumerateArray())
            {
                var date = DateTime.SpecifyKind(WbDates.CalendarDay(day.GetProperty("date").GetString()).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
                var requestedDay = DateOnly.FromDateTime(date);
                if (requestedStart.HasValue && (requestedDay < requestedStart.Value || requestedDay > requestedEnd!.Value))
                    throw new JsonException("WB вернул день вне запрошенного периода.");
                if (!seenDays.Add((campaign.Id, requestedDay)))
                    throw new JsonException("WB вернул повторный день кампании.");
                if (fillMissingOnly && await db.WbCampaignDailyChecks.AnyAsync(x => x.CampaignId == campaign.Id &&
                    x.Date == DateOnly.FromDateTime(date) && (x.Result == "data" || x.Result == "zero"), cancellationToken) &&
                    !await db.CampaignNomenclatureStatistics.AnyAsync(x => x.CampaignId == campaign.Id && x.Date == date &&
                        x.RecommendationFieldsVerified != true, cancellationToken)) continue;
                if (!await quality.ObserveAsync(storeId, "fullstats", campaign.Id.ToString(), requestedDay, requestedDay,
                    Number(day, "sum") == 0 ? "zero" : "data", cancellationToken)) continue;
                var previousArticles = await db.CampaignNomenclatureStatistics.AsNoTracking().Where(x => x.CampaignId == campaign.Id && x.Date == date)
                    .ToArrayAsync(cancellationToken);
                await db.CampaignStatistics.Where(s => s.CampaignId == campaign.Id && s.Date == date)
                    .ExecuteDeleteAsync(cancellationToken);
                await db.CampaignNomenclatureStatistics.Where(s => s.CampaignId == campaign.Id && s.Date == date)
                    .ExecuteDeleteAsync(cancellationToken);

                var spend = Number(day, "sum");
                if (spend < 0) throw new JsonException("WB вернул отрицательный расход.");
                var revenue = Number(day, "sum_price");
                var views = Whole(day, "views");
                var clicks = Whole(day, "clicks");
                db.CampaignStatistics.Add(new CampaignStatistics
                {
                    CampaignId = campaign.Id,
                    Date = date,
                    Spend = spend,
                    Revenue = revenue,
                    Impressions = views,
                    Clicks = clicks,
                    Carts = Whole(day, "atbs"),
                    Orders = Whole(day, "orders"),
                    Cancellations = Whole(day, "canceled"),
                    Ctr = views > 0 ? clicks * 100m / views : 0,
                    Drr = revenue > 0 ? spend * 100m / revenue : 0
                });
                var checkDate = DateOnly.FromDateTime(date);
                var check = await db.WbCampaignDailyChecks.SingleOrDefaultAsync(x =>
                    x.CampaignId == campaign.Id && x.Date == checkDate, cancellationToken);
                if (check == null)
                {
                    check = new WbCampaignDailyCheck { CampaignId = campaign.Id, Date = checkDate };
                    db.WbCampaignDailyChecks.Add(check);
                }
                check.Result = spend == 0 ? "zero" : "data";
                check.Spend = spend;
                check.JobId = jobId;
                check.CheckedAtUtc = observedAt;
                check.LastAttemptAtUtc = observedAt;
                check.LastAttemptResult = check.Result;
                rows++;
                acceptedCampaigns.Add(wbNumber);

                var articleStats = new Dictionary<string, CampaignNomenclatureStatistics>(StringComparer.Ordinal);
                if (day.TryGetProperty("apps", out var apps) && apps.ValueKind == JsonValueKind.Array)
                {
                    foreach (var app in apps.EnumerateArray())
                    {
                        if (!app.TryGetProperty("nms", out var nms) || nms.ValueKind != JsonValueKind.Array) continue;
                        foreach (var nm in nms.EnumerateArray())
                        {
                            var nmId = nm.GetProperty("nmId").GetInt64().ToString();
                            if (!nomenclatures.TryGetValue(nmId, out var article))
                            {
                                article = new Nomenclature
                                {
                                    Id = Guid.NewGuid(), StoreId = storeId, WbNomenclatureId = nmId,
                                    Name = nm.TryGetProperty("name", out var name) &&
                                        !string.IsNullOrWhiteSpace(name.GetString()) ? name.GetString()! : nmId
                                };
                                db.Nomenclatures.Add(article);
                                nomenclatures.Add(nmId, article);
                            }
                            if (!articleStats.TryGetValue(nmId, out var statistics))
                            {
                                statistics = new CampaignNomenclatureStatistics
                                {
                                    CampaignId = campaign.Id, NomenclatureId = article.Id,
                                    Date = date, OrderedProducts = 0, RecommendationFieldsVerified = true
                                };
                                articleStats.Add(nmId, statistics);
                            }
                            statistics.Spend += Number(nm, "sum");
                            statistics.Revenue += Number(nm, "sum_price");
                            // Only leaf nms totals: never add campaign/day/app shks to these units.
                            // Missing shks stays unknown, including when another app has a value.
                            statistics.OrderedProducts += nm.TryGetProperty("shks", out var units) &&
                                units.ValueKind == JsonValueKind.Number && units.TryGetInt32(out var quantity) && quantity >= 0
                                    ? quantity : (int?)null;
                            statistics.Impressions += Whole(nm, "views");
                            statistics.Clicks += Whole(nm, "clicks");
                            statistics.Carts += Whole(nm, "atbs");
                            statistics.Orders += Whole(nm, "orders");
                            statistics.Cancellations += Whole(nm, "canceled");
                        }
                    }
                }
                foreach (var statistics in articleStats.Values)
                {
                    await quality.ObserveAsync(storeId, "fullstats", $"{campaign.Id}:{statistics.NomenclatureId}", requestedDay, requestedDay,
                        statistics.Spend == 0 ? "zero" : "data", cancellationToken, proposed: statistics.Spend);
                    statistics.Ctr = statistics.Impressions > 0
                        ? statistics.Clicks * 100m / statistics.Impressions : null;
                    statistics.Cr = statistics.Clicks > 0
                        ? statistics.Orders * 100m / statistics.Clicks : null;
                    statistics.Cpc = statistics.Clicks > 0
                        ? statistics.Spend / statistics.Clicks : null;
                    statistics.Cpo = statistics.Orders > 0
                        ? statistics.Spend / statistics.Orders : null;
                    db.CampaignNomenclatureStatistics.Add(statistics);
                    if (recommendations?.Value.Enabled ?? true) db.WbArticleObservations.Add(new WbArticleObservation
                    {
                        Id = Guid.NewGuid(), CampaignId = statistics.CampaignId,
                        NomenclatureId = statistics.NomenclatureId, Date = DateOnly.FromDateTime(statistics.Date),
                        JobId = jobId, ObservedAtUtc = observedAt, Orders = statistics.Orders,
                        OrderedProducts = statistics.OrderedProducts, Revenue = statistics.Revenue
                    });
                }
                foreach (var missing in previousArticles.Where(x => !articleStats.ContainsKey(nomenclatures.Values.Single(n => n.Id == x.NomenclatureId).WbNomenclatureId)))
                {
                    var article = nomenclatures.Values.Single(x => x.Id == missing.NomenclatureId);
                    var entity = $"{campaign.Id}:{missing.NomenclatureId}";
                    await quality.ObserveAsync(storeId, "fullstats", entity, requestedDay, requestedDay, "zero", cancellationToken,
                        $"Успешный ответ WB без строки товара {article.WbNomenclatureId}: ноль за отчётный день.", previous: missing.Spend, proposed: 0);
                }
                // The former parser deleted missing leaves while retaining no_data quality.
                // Repair those leaves too when replaying the complete successful response.
                var presentEntities = articleStats.Values.Select(x => $"{campaign.Id}:{x.NomenclatureId}").ToHashSet();
                var absentEntities = await db.WbSourceQualities.Where(x => x.StoreId == storeId && x.Source == "fullstats" &&
                    x.Entity.StartsWith(campaign.Id + ":") && x.StartDate == requestedDay && x.EndDate == requestedDay)
                    .Select(x => x.Entity).ToArrayAsync(cancellationToken);
                foreach (var entity in absentEntities.Where(x => !presentEntities.Contains(x)))
                    await quality.ObserveAsync(storeId, "fullstats", entity, requestedDay, requestedDay, "zero", cancellationToken, proposed: 0);
            }
            }

            if (requestedIds != null && requestedStart.HasValue)
            {
                var requestedCampaigns = campaigns.Values.Where(x => long.TryParse(x.WbCampaignId, out var id) && requestedIds.Contains(id)).ToArray();
                var campaignIds = requestedCampaigns.Select(x => x.Id).ToArray();
                var previous = await db.WbCampaignDailyChecks.Where(x => campaignIds.Contains(x.CampaignId) &&
                    x.Date >= requestedStart.Value && x.Date <= requestedEnd!.Value)
                    .ToDictionaryAsync(x => (x.CampaignId, x.Date), cancellationToken);
                foreach (var campaign in requestedCampaigns)
                for (var date = requestedStart.Value; date <= requestedEnd!.Value; date = date.AddDays(1))
                {
                    if (seenDays.Contains((campaign.Id, date)) || WbCampaignEligibility.CreatedAfter(campaign, date) ||
                        WbCampaignEligibility.FinishedBefore(campaign, date)) continue;
                    if (fillMissingOnly && previous.TryGetValue((campaign.Id, date), out var preserved) &&
                        preserved.Result is "data" or "zero" && preserved.LastAttemptResult != "no_data") continue;
                    if (!await quality.ObserveAsync(storeId, "fullstats", campaign.Id.ToString(), date, date,
                        "zero", cancellationToken, "Успешный ответ WB без строки кампании за день: ноль.", proposed: 0)) continue;
                    if (!previous.TryGetValue((campaign.Id, date), out var check))
                    {
                        check = new WbCampaignDailyCheck { CampaignId = campaign.Id, Date = date,
                            CheckedAtUtc = observedAt, JobId = jobId };
                        db.WbCampaignDailyChecks.Add(check);
                    }
                    var childEntities = await db.WbSourceQualities.Where(x => x.StoreId == storeId && x.Source == "fullstats" &&
                        x.Entity.StartsWith(campaign.Id + ":") && x.StartDate == date && x.EndDate == date).Select(x => x.Entity).ToArrayAsync(cancellationToken);
                    foreach (var entity in childEntities)
                        await quality.ObserveAsync(storeId, "fullstats", entity, date, date, "zero", cancellationToken, proposed: 0);
                    var timestamp = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                    await db.CampaignStatistics.Where(x => x.CampaignId == campaign.Id && x.Date == timestamp).ExecuteDeleteAsync(cancellationToken);
                    await db.CampaignNomenclatureStatistics.Where(x => x.CampaignId == campaign.Id && x.Date == timestamp).ExecuteDeleteAsync(cancellationToken);
                    check.Result = "zero";
                    check.Spend = 0;
                    check.CheckedAtUtc = observedAt;
                    check.JobId = jobId;
                    check.LastAttemptAtUtc = observedAt;
                    check.LastAttemptResult = "zero";
                    acceptedCampaigns.Add(long.Parse(campaign.WbCampaignId));
                }
            }
            await db.SaveChangesAsync(cancellationToken);
            if (transaction != null) await transaction.CommitAsync(cancellationToken);
            return new WbFullStatsImportResult(rows, requestedIds?.Where(x => !acceptedCampaigns.Contains(x)).ToArray() ?? []);
        });
    }

    private static decimal Number(JsonElement value, string property) =>
        value.TryGetProperty(property, out var number) && number.ValueKind == JsonValueKind.Number
            ? number.GetDecimal() : throw new JsonException($"В fullstats отсутствует числовое поле {property}.");

    private static void ValidateDay(JsonElement day)
    {
        static void Metrics(JsonElement row)
        {
            foreach (var field in new[] { "sum", "sum_price" })
                if (Number(row, field) < 0) throw new JsonException("Отрицательный показатель WB.");
            foreach (var field in new[] { "views", "clicks", "atbs", "orders", "canceled" })
                if (Whole(row, field) < 0) throw new JsonException("Отрицательный показатель WB.");
        }
        Metrics(day);
        if (!day.TryGetProperty("apps", out var apps)) return;
        foreach (var app in apps.EnumerateArray())
        {
            foreach (var nm in app.GetProperty("nms").EnumerateArray())
            {
                _ = nm.GetProperty("nmId").GetInt64(); Metrics(nm);
                if (nm.TryGetProperty("name", out var name) && name.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    throw new JsonException("Некорректное имя товара.");
            }
        }
    }

    private static int Whole(JsonElement value, string property) =>
        value.TryGetProperty(property, out var number) && number.ValueKind == JsonValueKind.Number
            ? number.GetInt32() : throw new JsonException($"В fullstats отсутствует числовое поле {property}.");
}
