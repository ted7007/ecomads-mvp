using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Models;
using Ecomads.WebApplication.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

namespace Ecomads.WebApplication.Controllers;

[ApiController]
[Route("api/projects")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly EcomadsDbContext _context;
    public ProjectsController(EcomadsDbContext context) => _context = context;

    [HttpGet]
    public async Task<IActionResult> GetProjects([FromQuery] DateOnly? startDate, [FromQuery] DateOnly? endDate)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var sellerId))
        {
            return Unauthorized(new { message = "Недействительный токен" });
        }
        
        var moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        var yesterday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, moscow).DateTime).AddDays(-1);
        var end = endDate ?? yesterday;
        var start = startDate ?? end.AddDays(-29);
        if (start > end || end > yesterday || end.DayNumber - start.DayNumber > 365)
            return BadRequest(new { message = "Выберите завершённый период не длиннее 366 дней." });
        var expectedDays = end.DayNumber - start.DayNumber + 1;
        var startDateUtc = UtcDate.FromNullableDateOnly(start, DateTime.MinValue);
        var endDateUtc = UtcDate.FromNullableDateOnly(end, DateTime.MaxValue);

        var sellerStoreIds = await _context.Stores
            .Where(s => s.SellerId == sellerId)
            .Select(s => s.Id)
            .ToListAsync();
        
        var campaigns = await _context.Campaigns
            .Where(c => sellerStoreIds.Contains(c.StoreId) &&
                (c.IsActive || c.WbStatus == 11 || _context.CampaignStatistics.Any(s =>
                    s.CampaignId == c.Id && s.Date >= startDateUtc && s.Date <= endDateUtc)))
            .Select(c => new ProjectDashboardDto(
                c.Id,
                _context.WbCampaignNorms.Where(n => n.CampaignId == c.Id)
                    .Select(n => n.CustomName).FirstOrDefault() ?? c.Name,
                _context.CampaignStatistics
                    .Where(s => s.CampaignId == c.Id && s.Date >= startDateUtc && s.Date <= endDateUtc)
                    .GroupBy(s => 1)
                    .Select(g => new ProjectKpiDto(
                        g.Sum(x => x.Spend),
                        g.Sum(x => x.Revenue),
                        (decimal?)null,
                        g.Sum(x => x.Revenue) > 0 ? (g.Sum(x => x.Spend) / g.Sum(x => x.Revenue)) * 100 : 0,
                        (int)g.Sum(x => x.Clicks),
                        g.Sum(x => x.Impressions),
                        g.Sum(x => x.Impressions) > 0
                            ? g.Sum(x => x.Clicks) * 100 / g.Sum(x => x.Impressions)
                            : 0,
                        g.Count(),
                        expectedDays
                    ))
                    .FirstOrDefault() ?? new ProjectKpiDto(0, 0, null, 0, 0, 0, 0, 0, expectedDays)
            ))
            .ToListAsync();

        if (campaigns.Count > 0)
        {
            var campaignIds = campaigns.Select(x => x.Id).ToArray();
            var targets = await _context.Campaigns.AsNoTracking()
                .Where(x => campaignIds.Contains(x.Id))
                .Select(x => new
                {
                    x.Id,
                    CampaignTarget = _context.WbCampaignNorms.Where(n => n.CampaignId == x.Id)
                        .Select(n => n.TargetDrr).FirstOrDefault(),
                    Goal = _context.WbCampaignNorms.Where(n => n.CampaignId == x.Id)
                        .Select(n => n.Goal).FirstOrDefault(),
                    x.WbStatus,
                    StoreTarget = _context.WbStoreNorms.Where(n => n.StoreId == x.StoreId)
                        .Select(n => (decimal?)n.TargetDrr).FirstOrDefault()
                })
                .ToDictionaryAsync(x => x.Id);
            campaigns = campaigns.Select(x => x with
            {
                TargetDrr = targets[x.Id].CampaignTarget ?? targets[x.Id].StoreTarget ?? 30m,
                Goal = targets[x.Id].Goal,
                WbStatus = targets[x.Id].WbStatus
            }).ToList();
        }

        return Ok(campaigns);
    }
}
