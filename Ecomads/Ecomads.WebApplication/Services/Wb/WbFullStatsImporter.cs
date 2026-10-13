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
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var seenCampaigns = new HashSet<long>();
            var seenDays = new HashSet<(Guid CampaignId, DateOnly Date)>();
            var observedAt = DateTime.UtcNow;
            var rows = 0;
            foreach (var advert in root.EnumerateArray())
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
                var date = DateTime.SpecifyKind(day.GetProperty("date").GetDateTime().Date, DateTimeKind.Utc);
                var requestedDay = DateOnly.FromDateTime(date);
                if (requestedStart.HasValue && (requestedDay < requestedStart.Value || requestedDay > requestedEnd!.Value))
                    throw new JsonException("WB вернул день вне запрошенного периода.");
                if (!seenDays.Add((campaign.Id, requestedDay)))
                    throw new JsonException("WB вернул повторный день кампании.");
                if (fillMissingOnly && await db.WbCampaignDailyChecks.AnyAsync(x => x.CampaignId == campaign.Id &&
                    x.Date == DateOnly.FromDateTime(date) && (x.Result == "data" || x.Result == "zero"), cancellationToken)) continue;
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
                        JobId = jobId, ObservedAtUtc = DateTime.UtcNow, Orders = statistics.Orders,
                        OrderedProducts = statistics.OrderedProducts, Revenue = statistics.Revenue
                    });
                }
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
                    if (!previous.TryGetValue((campaign.Id, date), out var check))
                    {
                        check = new WbCampaignDailyCheck { CampaignId = campaign.Id, Date = date,
                            CheckedAtUtc = observedAt, JobId = jobId };
                        db.WbCampaignDailyChecks.Add(check);
                    }
                    // An absent row is an observation, never a zero or a reason to delete older values.
                    else if (fillMissingOnly && check.Result is "data" or "zero") continue;
                    check.LastAttemptAtUtc = observedAt;
                    check.LastAttemptResult = "no_data";
                }
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new WbFullStatsImportResult(rows, requestedIds?.Where(x => !seenCampaigns.Contains(x)).ToArray() ?? []);
        });
    }

    private static decimal Number(JsonElement value, string property) =>
        value.TryGetProperty(property, out var number) && number.ValueKind == JsonValueKind.Number
            ? number.GetDecimal() : throw new JsonException($"В fullstats отсутствует числовое поле {property}.");

    private static int Whole(JsonElement value, string property) =>
        value.TryGetProperty(property, out var number) && number.ValueKind == JsonValueKind.Number
            ? number.GetInt32() : throw new JsonException($"В fullstats отсутствует числовое поле {property}.");
}
