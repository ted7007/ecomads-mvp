using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Recommendations;

public sealed class ResultMetricsReader(EcomadsDbContext db)
{
    public async Task<ResultMetrics> ReadAsync(Campaign campaign, IReadOnlyList<ResultMember> members, RecommendationPeriod period, CancellationToken ct)
    {
        var start = period.Start; var end = period.End; var gaps = new HashSet<string>(); var sources = new HashSet<string>();
        var from = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); var until = end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var campaigns = await db.Campaigns.AsNoTracking().Where(x => x.StoreId == campaign.StoreId).ToArrayAsync(ct);
        var ids = campaigns.Select(x => x.Id).ToArray();
        var stats = await db.CampaignStatistics.AsNoTracking().Where(x => ids.Contains(x.CampaignId) && x.Date >= from && x.Date < until).ToArrayAsync(ct);
        var checks = await db.WbCampaignDailyChecks.AsNoTracking().Where(x => ids.Contains(x.CampaignId) && x.Date >= start && x.Date <= end).ToArrayAsync(ct);
        var quality = await db.WbSourceQualities.AsNoTracking().Where(x => x.StoreId == campaign.StoreId && x.StartDate <= end && x.EndDate >= start).ToArrayAsync(ct);
        var catalogue = await db.Nomenclatures.AsNoTracking().Where(x => x.StoreId == campaign.StoreId).ToArrayAsync(ct);
        var articleIds = catalogue.Select(x => x.Id).ToArray();
        var articles = await db.CampaignNomenclatureStatistics.AsNoTracking().Where(x => x.CampaignId == campaign.Id && articleIds.Contains(x.NomenclatureId) && x.Date >= from && x.Date < until).ToArrayAsync(ct);
        var clusters = await db.WbClusterStatistics.AsNoTracking().Where(x => x.CampaignId == campaign.Id && articleIds.Contains(x.NomenclatureId) && x.Date >= start && x.Date <= end).ToArrayAsync(ct);
        var productDays = await db.WbProductOrderDays.AsNoTracking().Where(x => x.StoreId == campaign.StoreId && x.Date >= start && x.Date <= end).ToArrayAsync(ct);
        var orders = await db.WbProductDailyOrders.AsNoTracking().Where(x => x.StoreId == campaign.StoreId && x.Date >= start && x.Date <= end).ToArrayAsync(ct);
        var stocks = await db.WbOperationalSnapshots.AsNoTracking().Where(x => x.StoreId == campaign.StoreId &&
            (x.Source == "stocks_fbw" || x.Source == "stocks_fbs") && x.RequestedAtUtc >= from.AddDays(-2) && x.RequestedAtUtc < until.AddDays(1))
            .OrderBy(x => x.RequestedAtUtc).ToArrayAsync(ct);
        var jobs = await db.WbSyncJobs.AsNoTracking().Where(x => x.StoreId == campaign.StoreId && x.Kind == "clusters" &&
            x.Status == "completed" && x.StartDate <= end && x.EndDate >= start).ToArrayAsync(ct);
        var coverage = jobs.Select(x => new ClusterCoverageInterval(x.StartDate, x.EndDate,
            JsonSerializer.Deserialize<WbNormQueryPair[]>(x.PairIdsJson ?? "[]") ?? [])).ToArray();
        bool Quality(string source, string entity, DateOnly day, bool required = false)
        {
            var q = quality.Where(x => x.Source == source && (x.Entity == "" || x.Entity == entity) && x.StartDate <= day && x.EndDate >= day)
                .OrderByDescending(x => x.LastAttemptAtUtc).FirstOrDefault();
            if (q?.AcceptedSnapshotId is { } id) sources.Add(id.ToString());
            return q == null ? !required : WbQuality.Confirmed(q.State);
        }
        var dates = Enumerable.Range(0, end.DayNumber - start.DayNumber + 1).Select(i => start.AddDays(i)).ToArray();
        var relevantCampaigns = campaigns.Where(c => c.Id == campaign.Id || c.WbStatus is 7 or 9 or 11 || stats.Any(s => s.CampaignId == c.Id)).ToArray();
        foreach (var c in relevantCampaigns)
            foreach (var day in dates)
            {
                var check = checks.SingleOrDefault(x => x.CampaignId == c.Id && x.Date == day);
                if (check?.Result is not ("data" or "zero") || check.LastAttemptResult == "no_data" && check.LastAttemptAtUtc >= check.CheckedAtUtc ||
                    !Quality("fullstats", c.Id.ToString(), day)) gaps.Add("Неполные дни рекламной статистики кампаний");
            }
        var objects = new List<ResultObjectMetrics>();
        foreach (var member in members.DistinctBy(x => (x.Card.Subject.NomenclatureId, x.Card.Subject.ClusterName)))
        {
            var card = member.Card; var subject = card.Subject; var nm = subject.NomenclatureId ?? "";
            var article = catalogue.SingleOrDefault(x => x.WbNomenclatureId == nm);
            if (article == null) { gaps.Add("Товар отсутствует в каталоге кабинета"); continue; }
            var rows = articles.Where(x => x.NomenclatureId == article.Id).ToArray();
            if (rows.Any(x => x.RecommendationFieldsVerified != true || x.OrderedProducts is null or < 0 || x.Revenue < 0 || x.Clicks < 0))
                gaps.Add("Непроверенные поля товарной статистики");
            var cluster = clusters.Where(x => x.NomenclatureId == article.Id && x.ClusterName == subject.ClusterName).ToArray();
            foreach (var day in dates)
            {
                if (!Quality("fullstats", $"{campaign.Id}:{article.Id}", day)) gaps.Add("Неполная товарная статистика");
                if (subject.Scope == "cluster" && (!long.TryParse(campaign.WbCampaignId, out var advert) || !long.TryParse(nm, out var parsedNm) ||
                    !RecommendationCoverage.Covers(new(day, day), advert, parsedNm, coverage) || !Quality("clusters", $"{campaign.Id}:{article.Id}", day, true)))
                    gaps.Add("Неполные дни кластерной статистики");
            }
            var totalKnown = true; var totalUnits = 0;
            foreach (var day in dates)
            {
                var header = productDays.SingleOrDefault(x => x.Date == day);
                var row = orders.SingleOrDefault(x => x.Date == day && x.NmId.ToString() == nm);
                if (header == null || !Quality("product_orders", "", day, true)) totalKnown = false;
                else { sources.Add(header.SnapshotId.ToString()); totalUnits += row?.OrderCount ?? 0; }
            }
            var adUnits = rows.Sum(x => x.OrderedProducts ?? 0); decimal? price = adUnits > 0 ? rows.Sum(x => x.Revenue) / adUnits : null;
            if (subject.Scope == "cluster" && cluster.Any(x => x.Clicks is null or < 0 || x.OrderedProducts is null or < 0)) gaps.Add("Нет кликов или рекламных единиц кластера");
            var knownDays = 0; var zeroDays = 0; var pausedZeroDays = 0;
            var states = await new ResultObservationReader(db).ReadAsync(campaign, card with { Presentation = card.Presentation! with {
                Primary = new("pause_campaign", "", Level: "campaign") } }, from.AddDays(-2), ct);
            foreach (var day in dates)
            {
                var midnight = TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), WbRefreshClock.Moscow);
                var endUtc = midnight.AddDays(1);
                decimal?[] quantities = [null, null];
                var zeroThroughoutDay = true;
                var index = 0;
                foreach (var source in new[] { "stocks_fbw", "stocks_fbs" })
                {
                    var inDay = stocks.Where(x => x.Source == source && x.State != "collecting" && x.RequestedAtUtc >= midnight && x.RequestedAtUtc < endUtc).ToArray();
                    if (inDay.Length > 0 && inDay.All(x => x.State == "ready"))
                    {
                        var totals = new List<decimal>();
                        foreach (var stock in inDay)
                        {
                            try { var positions = JsonSerializer.Deserialize<StockPosition[]>(stock.PayloadJson, ResultObservationReader.Json);
                                if (positions != null) { totals.Add(positions.Where(x => x.NmId.ToString() == nm).Sum(x => x.Quantity)); sources.Add(stock.Id.ToString()); } }
                            catch (JsonException) { }
                        }
                        if (totals.Count == inDay.Length) quantities[index] = totals.Min();
                        if (totals.Count != inDay.Length || totals.Any(x => x != 0)) zeroThroughoutDay = false;
                    }
                    else zeroThroughoutDay = false;
                    var atDayStart = stocks.LastOrDefault(x => x.Source == source && x.State != "collecting" && x.RequestedAtUtc <= midnight && x.RequestedAtUtc >= midnight.AddDays(-1));
                    if (atDayStart?.State != "ready") zeroThroughoutDay = false;
                    else try {
                        var positions = JsonSerializer.Deserialize<StockPosition[]>(atDayStart.PayloadJson, ResultObservationReader.Json);
                        if (positions == null || positions.Where(x => x.NmId.ToString() == nm).Sum(x => x.Quantity) != 0) zeroThroughoutDay = false;
                        else sources.Add(atDayStart.Id.ToString());
                    } catch (JsonException) { zeroThroughoutDay = false; }
                    index++;
                }
                if (quantities.All(x => x.HasValue))
                {
                    knownDays++;
                    if (quantities.Sum() == 0)
                    {
                        zeroDays++;
                        var observations = states.Where(x => x.AtUtc < endUtc && x.AtUtc >= midnight).ToArray();
                        var atStart = states.LastOrDefault(x => x.AtUtc <= midnight && x.AtUtc >= midnight.AddDays(-1));
                        if (zeroThroughoutDay && atStart != null && atStart.State == "ready" && (atStart.Status == 11 || atStart.Member == false) &&
                            observations.All(x => x.State == "ready" && (x.Status == 11 || x.Member == false))) pausedZeroDays++;
                    }
                }
            }
            var units = subject.Scope == "cluster" ? cluster.Sum(x => x.OrderedProducts ?? 0) : adUnits;
            objects.Add(new(nm + ":" + subject.ClusterName, nm, subject.Scope == "cluster" ? cluster.Sum(x => x.Spend) : rows.Sum(x => x.Spend),
                subject.Scope == "cluster" ? cluster.Sum(x => x.Clicks ?? 0) : rows.Sum(x => x.Clicks), units,
                subject.Scope == "cluster" ? price * units : rows.Sum(x => x.Revenue), totalKnown ? totalUnits : null,
                price, knownDays, zeroDays, pausedZeroDays));
        }
        return new(stats.Where(x => x.CampaignId == campaign.Id).Sum(x => x.Spend), stats.Where(x => x.CampaignId == campaign.Id).Sum(x => x.Revenue),
            stats.Where(x => x.CampaignId != campaign.Id && relevantCampaigns.Any(c => c.Id == x.CampaignId)).Sum(x => x.Spend), objects, gaps.ToArray(), sources.Order().ToArray());
    }
}
