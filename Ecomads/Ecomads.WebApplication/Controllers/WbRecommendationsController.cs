using System.Security.Claims;
using Ecomads.WebApplication.Services.Recommendations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ecomads.WebApplication.Controllers;

[ApiController]
[Authorize]
[Route("api/wb/campaigns/{campaignId:guid}/recommendations")]
public sealed class WbRecommendationsController(RecommendationService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid campaignId, [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var sellerId)) return Unauthorized();
        if (!startDate.HasValue || !endDate.HasValue || !new RecommendationWindow(startDate.Value, endDate.Value).IsValid)
            return BadRequest(new { message = "Передайте начало и конец периода от 1 до 366 дней." });
        var result = await service.CalculateAsync(sellerId, campaignId, new(startDate.Value, endDate.Value), ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("runs/{runId:guid}")]
    public async Task<IActionResult> Snapshot(Guid campaignId, Guid runId, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var sellerId)) return Unauthorized();
        var result = await service.SnapshotAsync(sellerId, campaignId, runId, ct);
        return result == null ? NotFound() : Ok(result);
    }
}
