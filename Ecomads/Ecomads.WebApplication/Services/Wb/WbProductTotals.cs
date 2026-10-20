using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Wb;

public sealed record WbProductTotal(decimal? OrderSum, decimal? AdvertisingSpend, decimal? Drr,
    int OrderDays, int AdvertisingDays, int ExpectedDays, string Status, string Reason);

public sealed class WbProductTotals(EcomadsDbContext db)
{
    public async Task<Dictionary<string, WbProductTotal>> GetAsync(Guid storeId, string[] articleIds,
        DateOnly start, DateOnly end, CancellationToken ct)
    {
        var expectedDays = end.DayNumber - start.DayNumber + 1;
        var dates = Enumerable.Range(0, expectedDays).Select(i => start.AddDays(i)).ToArray();
        var store = await db.Stores.AsNoTracking().SingleAsync(x => x.Id == storeId, ct);
        var campaigns = await db.Campaigns.AsNoTracking().Where(x => x.StoreId == storeId).ToArrayAsync(ct);
        var from = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = end.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var stats = await db.CampaignNomenclatureStatistics.AsNoTracking()
            .Where(x => x.Campaign.StoreId == storeId && x.Nomenclature.StoreId == storeId && x.Date >= from && x.Date <= to)
            .Include(x => x.Nomenclature).ToArrayAsync(ct);
        var withStats = stats.Select(x => x.CampaignId).ToHashSet();
        campaigns = campaigns.Where(x => x.WbStatus is 7 or 9 or 11 || withStats.Contains(x.Id)).ToArray();
        var campaignIds = campaigns.Select(x => x.Id).ToArray();
        var checks = (await db.WbCampaignDailyChecks.AsNoTracking().Where(x => campaignIds.Contains(x.CampaignId) &&
            x.Date >= start && x.Date <= end).ToArrayAsync(ct)).ToDictionary(x => (x.CampaignId, x.Date));
        var quality = await db.WbSourceQualities.AsNoTracking().Where(x => x.StoreId == storeId &&
            (x.Source == "fullstats" || x.Source == "product_orders") && x.StartDate <= end && x.EndDate >= start).ToArrayAsync(ct);
        var qualityMap = quality.GroupBy(x => (x.Source, x.Entity)).ToDictionary(x => x.Key, x => x.ToArray());
        WbSourceQuality? Latest(string source, string entity, DateOnly day) =>
            (qualityMap.GetValueOrDefault((source, entity)) ?? []).Concat(qualityMap.GetValueOrDefault((source, "")) ?? [])
            .Where(x => x.StartDate <= day && x.EndDate >= day).OrderByDescending(x => x.LastAttemptAtUtc).FirstOrDefault();
        var dayHeaders = (await db.WbProductOrderDays.AsNoTracking().Where(x => x.StoreId == storeId && x.Date >= start && x.Date <= end)
            .ToArrayAsync(ct)).ToDictionary(x => x.Date);
        var nmIds = articleIds.Where(x => long.TryParse(x, out _)).Select(long.Parse).ToArray();
        var orders = (await db.WbProductDailyOrders.AsNoTracking().Where(x => x.StoreId == storeId && x.Date >= start &&
            x.Date <= end && nmIds.Contains(x.NmId)).ToArrayAsync(ct)).ToDictionary(x => (x.NmId, x.Date));
        var campaignDayStats = stats.ToLookup(x => (x.CampaignId, DateOnly.FromDateTime(x.Date)));
        var articleStats = stats.ToLookup(x => (x.Nomenclature.WbNomenclatureId, DateOnly.FromDateTime(x.Date)));
        var articleMap = stats.Select(x => x.Nomenclature).DistinctBy(x => x.Id)
            .ToLookup(x => x.WbNomenclatureId, x => x.Id);
        var completeAdDays = new HashSet<DateOnly>();
        foreach (var day in dates)
        {
            if (store.CampaignsRefreshedAtUtc == null || store.ApiKey == null) continue;
            var relevant = campaigns.Where(c => campaignDayStats[(c.Id, day)].Any() ||
                (!WbCampaignEligibility.CreatedAfter(c, day) && !WbCampaignEligibility.FinishedBefore(c, day))).ToArray();
            if (relevant.All(c =>
            {
                var check = checks.GetValueOrDefault((c.Id, day));
                if (check == null || check.Result is not ("data" or "zero") || check.Spend == null ||
                    (check.LastAttemptResult == "no_data" && check.LastAttemptAtUtc >= check.CheckedAtUtc) ||
                    Latest("fullstats", c.Id.ToString(), day) is not { } q || !WbQuality.Confirmed(q.State)) return false;
                var leaves = campaignDayStats[(c.Id, day)].ToArray();
                // A missing product can be zero only in a confirmed, reconciled complete
                // campaign day. Never treat a missing campaign/day as zero spend.
                return leaves.All(x => x.Spend >= 0 && x.RecommendationFieldsVerified == true &&
                    Latest("fullstats", $"{c.Id}:{x.NomenclatureId}", day) is { } aq && WbQuality.Confirmed(aq.State)) &&
                    leaves.Sum(x => x.Spend) == check.Spend.Value;
            })) completeAdDays.Add(day);
        }
        var result = new Dictionary<string, WbProductTotal>();
        foreach (var article in articleIds)
        {
            long.TryParse(article, out var nmId);
            var orderDays = dates.Where(day => orders.ContainsKey((nmId, day)) && dayHeaders.TryGetValue(day, out var header) &&
                Latest("product_orders", "", day) is { } q && WbQuality.Confirmed(q.State) && q.AcceptedSnapshotId == header.SnapshotId).ToArray();
            var adDays = dates.Where(day => completeAdDays.Contains(day) && articleStats[(article, day)].All(x =>
                Latest("fullstats", $"{x.CampaignId}:{x.NomenclatureId}", day) is { } q && WbQuality.Confirmed(q.State))).ToArray();
            // Retained no_data observations of a formerly present leaf cannot become zero.
            var internalArticles = articleMap[article].ToArray();
            adDays = adDays.Where(day => campaigns.All(c => internalArticles.All(id =>
                Latest("fullstats", $"{c.Id}:{id}", day) is not { } q || WbQuality.Confirmed(q.State)))).ToArray();
            decimal? totalOrders = orderDays.Length == expectedDays ? orderDays.Sum(day => orders[(nmId, day)].OrderSum) : null;
            decimal? spend = adDays.Length == expectedDays ? dates.Sum(day => articleStats[(article, day)].Sum(x => x.Spend)) : null;
            decimal? drr = totalOrders > 0 && spend.HasValue ? Math.Round(spend.Value / totalOrders.Value * 100m, 2) : null;
            var full = totalOrders.HasValue && spend.HasValue;
            var status = full && end < WbSyncPlanner.Today() ? "complete" : orderDays.Length > 0 || adDays.Length > 0 ? "preliminary" : "unavailable";
            var reason = $"Заказы товара: {orderDays.Length} из {expectedDays} дней; расход всех кампаний кабинета: {adDays.Length} из {expectedDays} дней. " +
                (!full ? "Показатель без полного покрытия периода не рассчитывается. " : "") +
                (totalOrders == 0 ? "Сумма заказов равна нулю, общий ДРР не определён. " : "") +
                (end >= WbSyncPlanner.Today() ? "Текущий день не завершён. " : "") +
                "Общий ДРР = расход товара во всех кампаниях / все заказы товара × 100%.";
            result[article] = new(totalOrders, spend, drr, orderDays.Length, adDays.Length, expectedDays, status, reason);
        }
        return result;
    }
}
