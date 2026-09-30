using System.Security.Claims;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Controllers;

[ApiController]
[Authorize]
[Route("api/wb/recommendation-decisions")]
public sealed class WbRecommendationDecisionsController(EcomadsDbContext db) : ControllerBase
{
    public sealed record DecisionRequest(string RecommendationKey, DateOnly StartDate, DateOnly EndDate, string Status);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DateOnly startDate, [FromQuery] DateOnly endDate,
        CancellationToken cancellationToken)
    {
        if (!TrySeller(out var sellerId)) return Unauthorized();
        if (!ValidPeriod(startDate, endDate)) return BadRequest();
        var rows = await db.WbRecommendationDecisions.AsNoTracking()
            .Where(x => x.SellerId == sellerId && x.StartDate == startDate && x.EndDate == endDate)
            .Select(x => new { x.RecommendationKey, x.Status, x.UpdatedAtUtc })
            .ToListAsync(cancellationToken);
        return Ok(rows);
    }

    [HttpPut]
    public async Task<IActionResult> Save([FromBody] DecisionRequest request, CancellationToken cancellationToken)
    {
        if (!TrySeller(out var sellerId)) return Unauthorized();
        if (!ValidPeriod(request.StartDate, request.EndDate) ||
            string.IsNullOrWhiteSpace(request.RecommendationKey) || request.RecommendationKey.Length > 512 ||
            request.Status is not ("accepted" or "snoozed" or "irrelevant" or "open"))
            return BadRequest(new { message = "Проверьте рекомендацию, период и решение." });
        var key = request.RecommendationKey.Trim();
        var row = await db.WbRecommendationDecisions.SingleOrDefaultAsync(x =>
            x.SellerId == sellerId && x.RecommendationKey == key &&
            x.StartDate == request.StartDate && x.EndDate == request.EndDate, cancellationToken);
        if (request.Status == "open")
        {
            if (row != null) db.WbRecommendationDecisions.Remove(row);
            await db.SaveChangesAsync(cancellationToken);
            return Ok(new { recommendationKey = key, status = "open" });
        }
        if (row == null)
        {
            row = new WbRecommendationDecision { Id = Guid.NewGuid(), SellerId = sellerId,
                RecommendationKey = key, StartDate = request.StartDate, EndDate = request.EndDate };
            db.WbRecommendationDecisions.Add(row);
        }
        row.Status = request.Status;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { row.RecommendationKey, row.Status, row.UpdatedAtUtc });
    }

    private bool TrySeller(out Guid sellerId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out sellerId);

    private static bool ValidPeriod(DateOnly start, DateOnly end) =>
        start != default && end >= start && end.DayNumber - start.DayNumber <= 365;
}
