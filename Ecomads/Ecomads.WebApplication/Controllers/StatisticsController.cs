using System.Security.Claims;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Controllers;

[ApiController]
[Authorize]
[Route("api/statistics")]
public sealed class StatisticsController(EcomadsDbContext db) : ControllerBase
{
    [HttpGet("periods")]
    public async Task<IActionResult> GetLoadedPeriods(CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        var dates = await (from stat in db.CampaignStatistics.AsNoTracking()
            join campaign in db.Campaigns.AsNoTracking() on stat.CampaignId equals campaign.Id
            where campaign.Store.SellerId == sellerId
            select stat.Date)
            .Distinct().OrderByDescending(x => x)
            .Take(90).ToListAsync(cancellationToken);
        return Ok(dates.Select(date => new { startDate = date, endDate = date }));
    }

    [HttpGet("nomenclatures/{campaignId:guid}")]
    public async Task<IActionResult> GetNomenclatures(Guid campaignId, [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate, CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        if (!await db.Campaigns.AnyAsync(x => x.Id == campaignId && x.Store.SellerId == sellerId, cancellationToken))
            return NotFound();
        var query = db.CampaignNomenclatureStatistics.AsNoTracking().Where(x => x.CampaignId == campaignId);
        if (startDate.HasValue)
        {
            var from = DateTime.SpecifyKind(startDate.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            query = query.Where(x => x.Date >= from);
        }
        if (endDate.HasValue)
        {
            var to = DateTime.SpecifyKind(endDate.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            query = query.Where(x => x.Date <= to);
        }
        var raw = await query.Include(x => x.Nomenclature).ToListAsync(cancellationToken);
        var rows = raw.GroupBy(x => new { x.Nomenclature.WbNomenclatureId, x.Nomenclature.Name })
            .Select(group =>
            {
                var views = group.Sum(x => x.Impressions);
                var clicks = group.Sum(x => x.Clicks);
                var orders = group.Sum(x => x.Orders);
                var spend = group.Sum(x => x.Spend);
                return new NomenclatureStatisticsDto
                {
                    NomenclatureId = group.Key.WbNomenclatureId, Name = group.Key.Name,
                    Impressions = views, Clicks = clicks, Carts = group.Sum(x => x.Carts),
                    Orders = orders, Spend = spend, Revenue = group.Sum(x => x.Revenue),
                    Ctr = views > 0 ? clicks * 100m / views : null,
                    Cr = clicks > 0 ? orders * 100m / clicks : null,
                    Cpc = clicks > 0 ? spend / clicks : null,
                    Cpo = orders > 0 ? spend / orders : null
                };
            }).OrderByDescending(x => x.Spend).ToList();
        return Ok(rows);
    }

    private bool TrySellerId(out Guid sellerId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out sellerId);
}
