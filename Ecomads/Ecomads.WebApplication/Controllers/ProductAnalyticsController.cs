using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Models;
using Ecomads.WebApplication.Services.Analytics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Controllers;

[ApiController]
[Route("api/product-analytics")]
[Authorize]
public class ProductAnalyticsController : ControllerBase
{
    private readonly EcomadsDbContext _dbContext;
    private readonly IProductAnalyticsService _analyticsService;

    public ProductAnalyticsController(EcomadsDbContext dbContext, IProductAnalyticsService analyticsService)
    {
        _dbContext = dbContext;
        _analyticsService = analyticsService;
    }

    [HttpPost("events")]
    public async Task<IActionResult> TrackEvent([FromBody] ProductAnalyticsTrackRequest request)
    {
        if (!TryGetCurrentSellerId(out var sellerId))
        {
            return Unauthorized(new { message = "Недействительный токен" });
        }

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (request.EventName != ProductEvents.KeywordRecommendationOpened ||
            request.FeatureName != ProductFeatures.KeywordRecommendations)
        {
            return BadRequest(new { message = "Unsupported product analytics event." });
        }

        if (!request.CampaignId.HasValue ||
            !await SellerOwnsCampaignAsync(sellerId, request.CampaignId.Value))
        {
            return NotFound(new { message = "Кампания не найдена" });
        }

        if (request.KeywordId.HasValue &&
            !await KeywordBelongsToCampaignAsync(request.KeywordId.Value, request.CampaignId.Value))
        {
            return NotFound(new { message = "Ключ не найден" });
        }

        await _analyticsService.TrackAsync(new ProductUsageEventCreateDto
        {
            UserId = sellerId,
            EventName = request.EventName,
            FeatureName = request.FeatureName,
            CampaignId = request.CampaignId,
            KeywordId = request.KeywordId,
            Metadata = new
            {
                insightId = request.InsightId,
                recommendationStatus = request.RecommendationStatus,
                actionType = request.ActionType,
                priorityScore = request.PriorityScore,
                source = request.Source
            }
        }.WithRequestContext(HttpContext));

        return NoContent();
    }

    private bool TryGetCurrentSellerId(out Guid sellerId)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userId, out sellerId);
    }

    private async Task<bool> SellerOwnsCampaignAsync(Guid sellerId, Guid campaignId)
    {
        return await _dbContext.Compaigns.AnyAsync(campaign =>
            campaign.Id == campaignId &&
            campaign.Store.SellerId == sellerId);
    }

    private async Task<bool> KeywordBelongsToCampaignAsync(Guid keywordId, Guid campaignId)
    {
        return await _dbContext.KeywordStatistics.AnyAsync(keyword =>
            keyword.Id == keywordId &&
            keyword.CompaignId == campaignId);
    }
}

public class ProductAnalyticsTrackRequest
{
    [Required]
    public string EventName { get; set; } = string.Empty;

    [Required]
    public string FeatureName { get; set; } = string.Empty;

    public Guid? CampaignId { get; set; }

    public Guid? KeywordId { get; set; }

    public string? InsightId { get; set; }

    public string? RecommendationStatus { get; set; }

    public string? ActionType { get; set; }

    public double? PriorityScore { get; set; }

    public string? Source { get; set; }
}
