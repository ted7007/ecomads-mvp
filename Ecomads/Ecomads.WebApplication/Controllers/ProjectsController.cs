using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

namespace Ecomads.WebApplication.Controllers;

[ApiController]
[Route("api/projects")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly EcomadsDbContext _context;

    public ProjectsController(EcomadsDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetProjects([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var sellerId))
        {
            return Unauthorized(new { message = "Недействительный токен" });
        }
        
        if (startDate is null)
            startDate = DateTime.MinValue;
        
        if (endDate is null)
            endDate = DateTime.MaxValue;

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
                    .Where(s => s.CompaignId == c.Id && s.StartDate >= startDate && s.EndDate <= endDate)
                    .GroupBy(s => 1)
                    .Select(g => new ProjectKpiDto(
                        g.Sum(x => x.Spend),
                        g.Sum(x => x.Revenue),
                        g.Sum(x => x.Revenue) - g.Sum(x => x.Spend),
                        g.Sum(x => x.Revenue) > 0 ? (g.Sum(x => x.Spend) / g.Sum(x => x.Revenue)) * 100 : 0,
                        (int)g.Sum(x => x.Clicks),
                        g.Sum(x => x.Clicks) > 0
                            ? g.Sum(x => x.Ctr * x.Clicks) / g.Sum(x => x.Clicks)
                            : 0
                    ))
                    .FirstOrDefault() ?? new ProjectKpiDto(0, 0, 0, 0, 0, 0)
            ))
            .ToListAsync();

        return Ok(campaigns);
    }
}
