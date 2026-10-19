using System.Security.Claims;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Controllers;

[ApiController]
[Authorize]
[Route("api/wb/campaigns")]
public sealed class WbCampaignClustersController(EcomadsDbContext db) : ControllerBase
{
    public sealed record ClusterRow(string NomenclatureId, string NomenclatureName, string ClusterName,
        decimal Spend, int? Views, int? Clicks, int? Carts, int? Orders, decimal? Cpc,
        string Assessment, int? OrderedProducts, decimal? AverageOrderedUnitPrice,
        decimal? EstimatedOrderSum, decimal? EstimatedDrr, string EstimateStatus, string? EstimateReason);
    public sealed record ClusterResponse(bool IsWbConnected, bool IsPeriodComplete, IReadOnlyList<ClusterRow> Rows,
        int StoreNormVersion, int CampaignNormVersion);

    [HttpGet("{campaignId:guid}/clusters")]
    public async Task<IActionResult> Get(Guid campaignId, [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var sellerId)) return Unauthorized();
        var campaign = await db.Campaigns.AsNoTracking()
            .Where(x => x.Id == campaignId && x.Store.SellerId == sellerId)
            .Select(x => new { x.StoreId, x.WbCampaignId, IsWbConnected = x.Store.ApiKey != null })
            .SingleOrDefaultAsync(cancellationToken);
        if (campaign == null) return NotFound();
        if (startDate.HasValue && endDate.HasValue &&
            (startDate > endDate || endDate.Value.DayNumber - startDate.Value.DayNumber > 365))
            return BadRequest(new { message = "Выберите период не длиннее 366 дней." });
        if (!campaign.IsWbConnected) return Ok(new ClusterResponse(false, false, [], 0, 0));

        var completedJobs = await db.WbSyncJobs.AsNoTracking()
            .Where(x => x.StoreId == campaign.StoreId && x.Kind == "clusters" && x.Status == "completed" &&
                (!startDate.HasValue || x.StartDate <= startDate.Value) &&
                (!endDate.HasValue || x.EndDate >= endDate.Value))
            .Select(x => x.PairIdsJson).ToListAsync(cancellationToken);
        var isPeriodComplete = long.TryParse(campaign.WbCampaignId, out var wbCampaignId) &&
            completedJobs.Any(json => (JsonSerializer.Deserialize<WbNormQueryPair[]>(json ?? "[]") ?? [])
                .Any(pair => pair.AdvertId == wbCampaignId));

        var storeNorms = await db.WbStoreNorms.AsNoTracking()
            .SingleOrDefaultAsync(x => x.StoreId == campaign.StoreId, cancellationToken);
        var campaignNorms = await db.WbCampaignNorms.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CampaignId == campaignId, cancellationToken);
        var minClicks = campaignNorms?.MinClicks ?? storeNorms?.MinClicks ?? 30;
        var minSpend = campaignNorms?.MinSpend ?? storeNorms?.MinSpend ?? 500m;
        var minOrders = campaignNorms?.MinOrders ?? storeNorms?.MinOrders ?? 3;

        var query = from stat in db.WbClusterStatistics.AsNoTracking()
            join article in db.Nomenclatures.AsNoTracking() on stat.NomenclatureId equals article.Id
            where stat.CampaignId == campaignId
            select new { stat, article.WbNomenclatureId, article.Name };
        if (startDate.HasValue) query = query.Where(x => x.stat.Date >= startDate.Value);
        if (endDate.HasValue) query = query.Where(x => x.stat.Date <= endDate.Value);
        var raw = await query.ToListAsync(cancellationToken);
        var qualities = await db.WbSourceQualities.AsNoTracking().Where(x => x.StoreId == campaign.StoreId &&
            (x.Source == "clusters" || x.Source == "fullstats") &&
            (!startDate.HasValue || x.EndDate >= startDate.Value) && (!endDate.HasValue || x.StartDate <= endDate.Value))
            .ToArrayAsync(cancellationToken);
        bool SourceAccepted(string source, string entity, DateOnly day)
        {
            var latest = qualities.Where(x => x.Source == source && (x.Entity == "" || x.Entity == entity) &&
                    x.StartDate <= day && x.EndDate >= day).OrderByDescending(x => x.LastAttemptAtUtc).FirstOrDefault();
            return latest == null ? source != "clusters" : WbQuality.Confirmed(latest.State);
        }
        var staleArticles = raw.Where(x => !SourceAccepted("clusters", $"{campaignId}:{x.stat.NomenclatureId}", x.stat.Date))
            .Select(x => x.stat.NomenclatureId).ToHashSet();
        if (staleArticles.Count > 0) isPeriodComplete = false;
        // Estimates require an explicit selected period, never unbounded historical averages.
        var articleRows = new List<Ecomads.WebApplication.Data.Models.CampaignNomenclatureStatistics>();
        var fullStatsComplete = false;
        var estimateJobs = new List<(DateOnly Start, DateOnly End, WbNormQueryPair[] Pairs)>();
        if (startDate.HasValue && endDate.HasValue)
        {
            var from = DateTime.SpecifyKind(startDate.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var to = DateTime.SpecifyKind(endDate.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            articleRows = await db.CampaignNomenclatureStatistics.AsNoTracking()
                .Where(x => x.CampaignId == campaignId && x.Date >= from && x.Date <= to)
                .ToListAsync(cancellationToken);
            var checkedDates = await db.WbCampaignDailyChecks.AsNoTracking()
                .Where(x => x.CampaignId == campaignId && x.Date >= startDate.Value && x.Date <= endDate.Value &&
                    (x.Result == "data" || x.Result == "zero"))
                .Where(x => x.LastAttemptResult != "no_data" || x.LastAttemptAtUtc < x.CheckedAtUtc)
                .Select(x => x.Date).ToListAsync(cancellationToken);
            fullStatsComplete = checkedDates.Where(day => SourceAccepted("fullstats", campaignId.ToString(), day))
                .Distinct().Count() == endDate.Value.DayNumber - startDate.Value.DayNumber + 1;
            var jobs = await db.WbSyncJobs.AsNoTracking().Where(x => x.StoreId == campaign.StoreId &&
                x.Kind == "clusters" && x.Status == "completed" &&
                x.StartDate <= endDate.Value && x.EndDate >= startDate.Value)
                .Select(x => new { x.StartDate, x.EndDate, x.PairIdsJson }).ToListAsync(cancellationToken);
            estimateJobs = jobs.Select(x => (x.StartDate, x.EndDate,
                JsonSerializer.Deserialize<WbNormQueryPair[]>(x.PairIdsJson ?? "[]") ?? [])).ToList();
        }
        var articleById = articleRows.ToLookup(x => x.NomenclatureId);
        var rows = raw.GroupBy(x => new { x.WbNomenclatureId, x.Name, x.stat.ClusterName })
            .Select(group =>
            {
                var spend = group.Sum(x => x.stat.Spend);
                var clicks = group.All(x => x.stat.Clicks.HasValue) ? group.Sum(x => x.stat.Clicks!.Value) : (int?)null;
                var orderedProducts = group.All(x => x.stat.OrderedProducts.HasValue)
                    ? group.Sum(x => x.stat.OrderedProducts!.Value) : (int?)null;
                var articleId = group.First().stat.NomenclatureId;
                var pairComplete = !staleArticles.Contains(articleId) && startDate.HasValue && endDate.HasValue &&
                    long.TryParse(group.Key.WbNomenclatureId, out var nmId) &&
                    Enumerable.Range(0, endDate.Value.DayNumber - startDate.Value.DayNumber + 1)
                        .All(offset => estimateJobs.Any(job => job.Start <= startDate.Value.AddDays(offset) &&
                            job.End >= startDate.Value.AddDays(offset) &&
                            job.Pairs.Any(pair => pair.AdvertId == wbCampaignId && pair.NmId == nmId)));
                var estimate = startDate.HasValue && endDate.HasValue
                    ? WbClusterOrderEstimateCalculator.Calculate(startDate.Value, endDate.Value,
                        articleById[articleId].ToList(), group.Select(x => x.stat).ToList(), fullStatsComplete,
                        pairComplete, endDate.Value >= WbSyncPlanner.Today())
                    : new WbClusterOrderEstimate(null, null, null, "unavailable", "Выберите начало и конец периода.");
                return new ClusterRow(group.Key.WbNomenclatureId, group.Key.Name, group.Key.ClusterName,
                    spend,
                    group.All(x => x.stat.Views.HasValue) ? group.Sum(x => x.stat.Views!.Value) : null,
                    clicks,
                    group.All(x => x.stat.Carts.HasValue) ? group.Sum(x => x.stat.Carts!.Value) : null,
                    group.All(x => x.stat.Orders.HasValue) ? group.Sum(x => x.stat.Orders!.Value) : null,
                    clicks is > 0 ? Math.Round(spend / clicks.Value, 2) : null,
                    staleArticles.Contains(articleId) ? "Данные устарели: последний ответ WB не подтвердил полный снимок." : Assess(spend, clicks,
                        group.All(x => x.stat.Orders.HasValue) ? group.Sum(x => x.stat.Orders!.Value) : null,
                        minSpend, minClicks, minOrders), orderedProducts, estimate.AverageOrderedUnitPrice,
                    estimate.EstimatedOrderSum, estimate.EstimatedDrr, estimate.Status, estimate.Reason);
            })
            .OrderByDescending(x => x.Spend)
            .ToList();
        return Ok(new ClusterResponse(true, isPeriodComplete, rows, storeNorms?.Version ?? 0, campaignNorms?.Version ?? 0));
    }

    private static string Assess(decimal spend, int? clicks, int? orders,
        decimal minSpend, int minClicks, int minOrders)
    {
        if (!clicks.HasValue || !orders.HasValue) return "Показатели неполные";
        if (spend >= minSpend && clicks >= minClicks && orders == 0)
            return "Расход без заказов — проверить";
        if (spend < minSpend && clicks < minClicks && orders < minOrders)
            return "Недостаточно данных";
        return "Наблюдать: выручка кластера недоступна";
    }
}
