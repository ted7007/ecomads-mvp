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
        
        var campaigns = await _context.Compaigns
            .Select(c => new ProjectDashboardDto(
                c.Id,
                c.Name,
                _context.CompaignStatistics
                    .Where(s => s.CompaignId == c.Id && s.StartDate >= startDate && s.EndDate <= endDate)
                    .GroupBy(s => 1)
                    .Select(g => new ProjectKpiDto(
                        g.Sum(x => x.Spend),
                        g.Average(x => x.Drr),
                        (int)g.Sum(x => x.Clicks),
                        g.Average(x => x.Ctr)
                    ))
                    .FirstOrDefault() ?? new ProjectKpiDto(0, 0, 0, 0)
            ))
            .ToListAsync();

        return Ok(campaigns);
    }
}
