using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Models;
using Ecomads.WebApplication.Services.Analytics;
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
    private readonly IProductAnalyticsService _analyticsService;
    private readonly ILogger<ProjectsController> _logger;

    public ProjectsController(
        EcomadsDbContext context,
        IProductAnalyticsService analyticsService,
        ILogger<ProjectsController> logger)
    {
        _context = context;
        _analyticsService = analyticsService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetProjects([FromQuery] DateOnly? startDate, [FromQuery] DateOnly? endDate, [FromQuery] string? source)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var sellerId))
        {
            return Unauthorized(new { message = "Недействительный токен" });
        }
        
        var startDateUtc = UtcDate.FromNullableDateOnly(startDate, DateTime.MinValue);
        var endDateUtc = UtcDate.FromNullableDateOnly(endDate, DateTime.MaxValue);

        var sellerStoreIds = await _context.Stores
            .Where(s => s.SellerId == sellerId)
            .Select(s => s.Id)
            .ToListAsync();
        
        var campaigns = await _context.Compaigns
            .Where(c => sellerStoreIds.Contains(c.StoreId))
            .Select(c => new ProjectDashboardDto(
                c.Id,
                c.Name,
                _context.CompaignStatistics
                    .Where(s => s.CompaignId == c.Id && s.StartDate >= startDateUtc && s.EndDate <= endDateUtc)
                    .GroupBy(s => 1)
                    .Select(g => new ProjectKpiDto(
                        g.Sum(x => x.Spend),
                        g.Sum(x => x.Revenue),
                        g.Sum(x => x.Revenue),
                        g.Sum(x => x.Revenue) > 0 ? (g.Sum(x => x.Spend) / g.Sum(x => x.Revenue)) * 100 : 0,
                        (int)g.Sum(x => x.Clicks),
                        g.Sum(x => x.Clicks) > 0
                            ? g.Sum(x => x.Ctr * x.Clicks) / g.Sum(x => x.Clicks)
                            : 0
                    ))
                    .FirstOrDefault() ?? new ProjectKpiDto(0, 0, 0, 0, 0, 0)
            ))
            .ToListAsync();

        if (string.Equals(source, "dashboard", StringComparison.OrdinalIgnoreCase))
        {
            await _analyticsService.TrackAsync(new ProductUsageEventCreateDto
            {
                UserId = sellerId,
                EventName = ProductEvents.DashboardViewed,
                FeatureName = ProductFeatures.Dashboard,
                Metadata = new
                {
                    startDate = startDateUtc,
                    endDate = endDateUtc,
                    campaignsCount = campaigns.Count
                }
            }.WithRequestContext(HttpContext));

            _logger.LogInformation(
                "Dashboard opened by user {UserId}. CampaignsCount: {CampaignsCount}, StartDate: {StartDate}, EndDate: {EndDate}",
                sellerId,
                campaigns.Count,
                startDateUtc,
                endDateUtc);
        }

        return Ok(campaigns);
    }
}
