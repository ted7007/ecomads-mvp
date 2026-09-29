using System.Security.Claims;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Controllers;

[ApiController]
[Authorize]
[Route("api/wb/campaigns")]
public sealed class WbCampaignTrendController(EcomadsDbContext db) : ControllerBase
{
    [HttpGet("{campaignId:guid}/spend-trend")]
    public async Task<IActionResult> Get(Guid campaignId, [FromQuery] DateOnly? endDate,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var sellerId)) return Unauthorized();
        var storeId = await db.Campaigns.AsNoTracking()
            .Where(x => x.Id == campaignId && x.Store.SellerId == sellerId)
            .Select(x => (Guid?)x.StoreId).SingleOrDefaultAsync(cancellationToken);
        if (!storeId.HasValue) return NotFound();

        var moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        var yesterday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, moscow).DateTime).AddDays(-1);
        var end = endDate ?? yesterday;
        if (end > yesterday) return BadRequest(new { message = "Сравнение доступно только за завершённые дни." });
        var from = DateTime.SpecifyKind(end.AddDays(-7).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(end.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var rows = await db.CampaignStatistics.AsNoTracking().Where(x => x.CampaignId == campaignId &&
            x.Date >= from && x.Date <= to).ToListAsync(cancellationToken);
        var storeNorms = await db.WbStoreNorms.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == storeId,
            cancellationToken);
        var campaignNorms = await db.WbCampaignNorms.AsNoTracking().SingleOrDefaultAsync(x => x.CampaignId == campaignId,
            cancellationToken);
        var threshold = campaignNorms?.DeviationPercent ?? storeNorms?.DeviationPercent ?? 40m;
        return Ok(WbSpendTrendCalculator.Calculate(end, rows, threshold,
            storeNorms?.Version ?? 0, campaignNorms?.Version ?? 0));
    }
}
