using System.Security.Claims;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Recommendations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Controllers;

[ApiController]
[Authorize]
[Route("api/wb/norms/campaigns/{campaignId:guid}/articles")]
public sealed class WbArticleNormsController(EcomadsDbContext db) : ControllerBase
{
    public sealed record ArticleNormRequest(decimal? TargetDrr);
    public sealed record ArticleNormRow(Guid ArticleId, string NomenclatureId, string Name,
        decimal? TargetDrr, decimal EffectiveTargetDrr, string Source, int Version);

    [HttpGet]
    public async Task<IActionResult> Get(Guid campaignId, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var sellerId)) return Unauthorized();
        var storeId = await db.Campaigns.Where(x => x.Id == campaignId && x.Store.SellerId == sellerId)
            .Select(x => (Guid?)x.StoreId).SingleOrDefaultAsync(ct);
        if (!storeId.HasValue) return NotFound();
        var storeNorm = await db.WbStoreNorms.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == storeId, ct);
        var campaignNorm = await db.WbCampaignNorms.AsNoTracking().SingleOrDefaultAsync(x => x.CampaignId == campaignId, ct);
        var norms = await db.WbArticleNorms.AsNoTracking().Where(x => x.CampaignId == campaignId).ToDictionaryAsync(x => x.NomenclatureId, ct);
        var articles = await db.Nomenclatures.AsNoTracking().Where(x => x.StoreId == storeId &&
            (db.CampaignNomenclatureStatistics.Any(s => s.CampaignId == campaignId && s.NomenclatureId == x.Id) ||
             db.WbClusterStatistics.Any(s => s.CampaignId == campaignId && s.NomenclatureId == x.Id)))
            .OrderBy(x => x.WbNomenclatureId).ToListAsync(ct);
        return Ok(articles.Select(article =>
        {
            norms.TryGetValue(article.Id, out var norm);
            var effective = RecommendationEngine.ResolveNorm(norm?.TargetDrr, campaignNorm?.TargetDrr, storeNorm?.TargetDrr);
            return new ArticleNormRow(article.Id, article.WbNomenclatureId, article.Name, norm?.TargetDrr,
                effective.Value * 100, effective.Source, norm?.Version ?? 0);
        }));
    }

    [HttpPut("{articleId:guid}")]
    public async Task<IActionResult> Put(Guid campaignId, Guid articleId, ArticleNormRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var sellerId)) return Unauthorized();
        var storeId = await db.Campaigns.Where(x => x.Id == campaignId && x.Store.SellerId == sellerId)
            .Select(x => (Guid?)x.StoreId).SingleOrDefaultAsync(ct);
        if (!storeId.HasValue || !await db.Nomenclatures.AnyAsync(x => x.Id == articleId && x.StoreId == storeId &&
            (db.CampaignNomenclatureStatistics.Any(s => s.CampaignId == campaignId && s.NomenclatureId == articleId) ||
             db.WbClusterStatistics.Any(s => s.CampaignId == campaignId && s.NomenclatureId == articleId)), ct)) return NotFound();
        if (request.TargetDrr is < 0 or > 1000 || request.TargetDrr.HasValue && decimal.Round(request.TargetDrr.Value, 2) != request.TargetDrr)
            return BadRequest(new { message = "ДРР должен быть от 0 до 1000%, не более двух знаков после запятой." });
        return await db.Database.CreateExecutionStrategy().ExecuteAsync<IActionResult>(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({campaignId.ToString()}, 0))", ct);
            var norm = await db.WbArticleNorms.SingleOrDefaultAsync(x => x.CampaignId == campaignId && x.NomenclatureId == articleId, ct);
            if (norm == null)
            {
                norm = new WbArticleNorm { CampaignId = campaignId, NomenclatureId = articleId };
                db.WbArticleNorms.Add(norm);
            }
            else norm.Version++;
            norm.TargetDrr = request.TargetDrr;
            norm.UpdatedAtUtc = DateTime.UtcNow;
            db.WbNormRevisions.Add(new WbNormRevision { Id = Guid.NewGuid(), StoreId = storeId.Value,
                CampaignId = campaignId, NomenclatureId = articleId, Version = norm.Version,
                SettingsJson = JsonSerializer.Serialize(request), CreatedAtUtc = norm.UpdatedAtUtc });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Ok(new { norm.TargetDrr, norm.Version });
        });
    }
}
