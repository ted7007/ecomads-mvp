using Microsoft.AspNetCore.Mvc;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Models;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Controllers;

[ApiController]
[Route("api/projects")]
public class ProjectsController : ControllerBase
{
    private readonly EcomadsDbContext _context;

    public ProjectsController(EcomadsDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetProjects([FromQuery] DateTime startDate, [FromQuery] DateTime endDate)
    {
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
