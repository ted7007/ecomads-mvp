using System.Security.Claims;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ecomads.WebApplication.Controllers;

[ApiController]
[Authorize]
[Route("api/statistics/coverage")]
public sealed class WbCoverageController(WbDataCoverageService coverage) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateOnly startDate, [FromQuery] DateOnly endDate,
        [FromQuery] Guid? campaignId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var sellerId)) return Unauthorized();
        if (startDate == default || endDate < startDate || endDate.DayNumber - startDate.DayNumber > 365 ||
            endDate > WbSyncPlanner.Yesterday()) return BadRequest();
        return Ok(await coverage.GetAsync(sellerId, startDate, endDate, campaignId, cancellationToken));
    }
}
