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
        string Assessment);
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
        var rows = raw.GroupBy(x => new { x.WbNomenclatureId, x.Name, x.stat.ClusterName })
            .Select(group =>
            {
                var spend = group.Sum(x => x.stat.Spend);
                var clicks = group.All(x => x.stat.Clicks.HasValue) ? group.Sum(x => x.stat.Clicks!.Value) : (int?)null;
                return new ClusterRow(group.Key.WbNomenclatureId, group.Key.Name, group.Key.ClusterName,
                    spend,
                    group.All(x => x.stat.Views.HasValue) ? group.Sum(x => x.stat.Views!.Value) : null,
                    clicks,
                    group.All(x => x.stat.Carts.HasValue) ? group.Sum(x => x.stat.Carts!.Value) : null,
                    group.All(x => x.stat.Orders.HasValue) ? group.Sum(x => x.stat.Orders!.Value) : null,
                    clicks is > 0 ? Math.Round(spend / clicks.Value, 2) : null,
                    Assess(spend, clicks,
                        group.All(x => x.stat.Orders.HasValue) ? group.Sum(x => x.stat.Orders!.Value) : null,
                        minSpend, minClicks, minOrders));
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
