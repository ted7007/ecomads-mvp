using System.Security.Claims;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Controllers;

[ApiController]
[Authorize]
[Route("api/wb/stores/{storeId:guid}/report-readiness")]
public sealed class WbReportReadinessController(EcomadsDbContext db, WbReportReadinessService readiness) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid storeId, [FromQuery] DateOnly? reportDate, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var sellerId)) return Unauthorized();
        if (!await db.Stores.AnyAsync(x => x.Id == storeId && x.SellerId == sellerId && x.ApiKey != null, ct)) return NotFound();
        var date = reportDate ?? WbSyncPlanner.Yesterday();
        if (date == default || date > WbSyncPlanner.Yesterday()) return BadRequest();
        return Ok(await readiness.GetAsync(storeId, date, ct));
    }
}
