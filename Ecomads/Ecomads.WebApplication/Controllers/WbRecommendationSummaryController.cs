using System.Security.Claims;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Services.Recommendations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Controllers;

[ApiController, Authorize, Route("api/wb/recommendations/summary")]
public sealed class WbRecommendationSummaryController(RecommendationService service, EcomadsDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid? storeId, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var seller)) return Unauthorized();
        var window = RecommendationPeriods.Economic(DateTime.UtcNow);
        if (storeId.HasValue && !await db.Stores.AnyAsync(x => x.Id == storeId && x.SellerId == seller, ct)) return NotFound();
        var reports = await service.CalculateManyAsync(seller, storeId, window, ct);
        window = reports.FirstOrDefault()?.Window ?? window;
        var cards = reports.SelectMany(r => r.Cards.Select(card => new { r.RunId, Card = card }))
            .OrderBy(x => x.Card.Rule.Group).ThenBy(x => x.Card.Presentation?.Priority ?? 4).ThenByDescending(x => x.Card.Presentation?.WeeklySpend ?? x.Card.Facts.Spend / window.Days * 7)
            .ThenBy(x => x.Card.Subject.CampaignId).ThenBy(x => x.Card.Subject.NomenclatureId, StringComparer.Ordinal).ThenBy(x => x.Card.Subject.ClusterName, StringComparer.Ordinal).ToArray();
        return Ok(new { schemaVersion = 3, window, enabled = reports.Any(x => x.Enabled), top = cards.Take(5), remaining = Math.Max(0, cards.Length - 5),
            campaigns = reports.Select(r => new { campaignId = r.CampaignId, name = r.CampaignName ?? "Кампания",
                r.RunId, r.Status, r.Message, count = r.Cards.Count,
                weeklySpend = r.Cards.Where(c => c.Subject.Scope == "cluster").DistinctBy(c => (c.Subject.ArticleId, c.Subject.ClusterName)).Sum(c => c.Presentation?.WeeklySpend ?? c.Facts.Spend / window.Days * 7) }) });
    }
}
