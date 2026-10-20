using System.Security.Claims;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Services.Recommendations;
using Ecomads.WebApplication.Services.Wb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ecomads.WebApplication.Controllers;

[ApiController, Authorize]
public sealed class WbResultsController(EcomadsDbContext db, ResultsService service, TimeProvider clock,
    IOptions<RecommendationOptions> options) : ControllerBase
{
    [HttpPost("api/wb/recommendations/exposures")]
    public async Task<IActionResult> Exposures(IReadOnlyList<ResultExposureRequest> requests, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var seller)) return Unauthorized();
        if (requests == null || requests.Count is < 1 or > 100) return BadRequest(new { message = "Укажите от 1 до 100 показанных карточек." });
        if (!options.Value.ResultsEnabled) return NoContent();
        return await service.RegisterAsync(seller, requests, ct) ? NoContent() : NotFound();
    }
    [HttpGet("api/wb/results")]
    public async Task<IActionResult> Get(Guid? storeId, string period = "month", DateOnly? startDate = null, DateOnly? endDate = null,
        string filter = "all", int page = 0, int pageSize = 10, CancellationToken ct = default)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var seller)) return Unauthorized();
        if (storeId != null && !await db.Stores.AnyAsync(x => x.Id == storeId && x.SellerId == seller, ct)) return NotFound();
        var today = WbSyncPlanner.Today(clock.GetUtcNow().UtcDateTime); var end = endDate ?? today;
        DateOnly? start = startDate ?? (period == "all" ? null : period == "30" ? end.AddDays(-29) : new DateOnly(end.Year, end.Month, 1));
        if (period is not ("month" or "30" or "all") || filter is not ("all" or "result" or "measuring" or "not_applied" or "rejected") ||
            page < 0 || pageSize is not (5 or 10 or 20 or 100) || end > today || start > end)
            return BadRequest(new { message = "Проверьте период, фильтр и страницу." });
        return Ok(await new ResultsReportReader(db, clock).ReadAsync(seller, storeId, start, end, filter, page, pageSize, options.Value.ResultsEnabled, ct));
    }
    [HttpGet("api/wb/results/{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var seller)) return Unauthorized();
        var detail = await new ResultsReportReader(db, clock).DetailAsync(seller, id, ct);
        return detail == null ? NotFound() : Ok(detail);
    }
}
