using System.Security.Claims;
using Ecomads.WebApplication.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ecomads.WebApplication.Services.Wb;

namespace Ecomads.WebApplication.Controllers;

[ApiController]
[Authorize]
[Route("api/statistics/daily")]
public sealed class WbDailySeriesController(EcomadsDbContext db, WbDataCoverageService coverage) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateOnly startDate, [FromQuery] DateOnly endDate,
        [FromQuery] Guid? campaignId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var sellerId)) return Unauthorized();
        var moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        var yesterday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, moscow).DateTime)
            .AddDays(-1);
        if (startDate == default || endDate < startDate || endDate > yesterday ||
            endDate.DayNumber - startDate.DayNumber > 365)
            return BadRequest(new { message = "Выберите завершённый период не длиннее 366 дней." });

        var campaigns = await db.Campaigns.AsNoTracking()
            .Where(x => x.Store.SellerId == sellerId && (!campaignId.HasValue || x.Id == campaignId.Value))
            .Select(x => new { x.Id, x.IsActive, x.WbStatus })
            .ToListAsync(cancellationToken);
        if (campaignId.HasValue && campaigns.Count == 0) return NotFound();
        var ids = campaigns.Select(x => x.Id).ToArray();
        var from = DateTime.SpecifyKind(startDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(endDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var rows = await db.CampaignStatistics.AsNoTracking()
            .Where(x => ids.Contains(x.CampaignId) && x.Date >= from && x.Date <= to)
            .Select(x => new { x.CampaignId, x.Date, x.Spend, x.Revenue, x.Clicks, x.Impressions, x.Orders })
            .ToListAsync(cancellationToken);
        var grouped = rows.GroupBy(x => DateOnly.FromDateTime(x.Date))
            .ToDictionary(x => x.Key, x => new
            {
                Spend = x.Sum(row => row.Spend), Revenue = x.Sum(row => row.Revenue),
                Clicks = x.Sum(row => row.Clicks), Impressions = x.Sum(row => row.Impressions),
                Orders = x.Sum(row => row.Orders), LoadedCampaigns = x.Select(row => row.CampaignId).Distinct().Count()
            });
        var storeIds = campaignId.HasValue ? [] : await db.Stores.AsNoTracking()
            .Where(x => x.SellerId == sellerId && x.ApiKey != null)
            .Select(x => x.Id).ToArrayAsync(cancellationToken);
        var orderRows = storeIds.Length == 0 ? [] : await db.WbStoreDailyOrders.AsNoTracking()
            .Where(x => storeIds.Contains(x.StoreId) && x.Date >= startDate && x.Date <= endDate)
            .Select(x => new { x.StoreId, x.Date, x.OrderSum, x.OrderCount })
            .ToArrayAsync(cancellationToken);
        var dailyOrders = orderRows.GroupBy(x => x.Date).ToDictionary(x => x.Key, x => new
        {
            StoreCount = x.Select(row => row.StoreId).Distinct().Count(),
            Sum = x.Sum(row => row.OrderSum), Count = x.Sum(row => row.OrderCount)
        });
        var verifiedPeriod = campaignId.HasValue ? null : await coverage.GetAsync(sellerId, startDate, endDate,
            null, cancellationToken);
        var verifiedDays = verifiedPeriod?.Days.ToDictionary(x => x.Date);
        var expectedCampaigns = campaignId.HasValue ? 1 : campaigns.Count(x => x.IsActive || x.WbStatus == 11);
        var days = Enumerable.Range(0, endDate.DayNumber - startDate.DayNumber + 1)
            .Select(offset =>
            {
                var date = startDate.AddDays(offset);
                var found = grouped.TryGetValue(date, out var value);
                var completeOrders = dailyOrders.TryGetValue(date, out var order) &&
                    storeIds.Length > 0 && order!.StoreCount == storeIds.Length;
                decimal? spend = campaignId.HasValue ? found ? value!.Spend : null :
                    verifiedDays?.GetValueOrDefault(date)?.Spend;
                decimal? totalOrderSum = completeOrders ? order!.Sum : null;
                return new
                {
                    Date = date.ToString("yyyy-MM-dd"),
                    Spend = spend,
                    Revenue = found ? (decimal?)value!.Revenue : null,
                    Clicks = found ? (int?)value!.Clicks : null,
                    Impressions = found ? (int?)value!.Impressions : null,
                    Orders = found ? (int?)value!.Orders : null,
                    TotalOrderSum = totalOrderSum,
                    TotalOrderCount = completeOrders ? (int?)order!.Count : null,
                    TotalDrr = verifiedDays?.GetValueOrDefault(date)?.Drr,
                    Drr = found && value!.Revenue > 0 ? (decimal?)(value.Spend / value.Revenue * 100m) : null,
                    Ctr = found && value!.Impressions > 0 ? (decimal?)(value.Clicks * 100m / value.Impressions) : null,
                    LoadedCampaigns = found ? value!.LoadedCampaigns : 0,
                    ExpectedCampaigns = expectedCampaigns
                };
            });
        return Ok(days);
    }
}
