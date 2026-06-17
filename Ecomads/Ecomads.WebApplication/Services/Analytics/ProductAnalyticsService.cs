using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Models;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Analytics;

public interface IProductAnalyticsService
{
    Task TrackAsync(ProductUsageEventCreateDto dto);
}

public class ProductAnalyticsService : IProductAnalyticsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly EcomadsDbContext _dbContext;
    private readonly ILogger<ProductAnalyticsService> _logger;

    public ProductAnalyticsService(EcomadsDbContext dbContext, ILogger<ProductAnalyticsService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task TrackAsync(ProductUsageEventCreateDto dto)
    {
        ProductUsageEvent? productUsageEvent = null;

        try
        {
            if (string.IsNullOrWhiteSpace(dto.EventName) || string.IsNullOrWhiteSpace(dto.FeatureName))
            {
                _logger.LogWarning("Product analytics event skipped because event or feature name is empty");
                return;
            }

            productUsageEvent = new ProductUsageEvent
            {
                Id = Guid.NewGuid(),
                UserId = dto.UserId,
                EventName = dto.EventName,
                FeatureName = dto.FeatureName,
                CampaignId = dto.CampaignId,
                KeywordId = dto.KeywordId,
                LlmUsageId = dto.LlmUsageId,
                MetadataJson = dto.Metadata == null ? null : JsonSerializer.Serialize(dto.Metadata, JsonOptions),
                Path = Truncate(dto.Path, 500),
                Method = Truncate(dto.Method, 20),
                UserAgent = Truncate(dto.UserAgent, 500),
                IpHash = Truncate(dto.IpHash, 128),
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.ProductUsageEvents.Add(productUsageEvent);
            await _dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            if (productUsageEvent != null)
            {
                _dbContext.Entry(productUsageEvent).State = EntityState.Detached;
            }

            _logger.LogError(
                ex,
                "Failed to save product analytics event. EventName: {EventName}, FeatureName: {FeatureName}, UserId: {UserId}",
                dto.EventName,
                dto.FeatureName,
                dto.UserId);
        }
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
