using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Models;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Analytics;

public interface ILlmUsageTrackingService
{
    Task<Guid?> TrackSuccessAsync(LlmUsageSuccessDto dto);

    Task<Guid?> TrackFailureAsync(LlmUsageFailureDto dto);
}

public class LlmUsageTrackingService : ILlmUsageTrackingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly EcomadsDbContext _dbContext;
    private readonly ILogger<LlmUsageTrackingService> _logger;

    public LlmUsageTrackingService(EcomadsDbContext dbContext, ILogger<LlmUsageTrackingService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<Guid?> TrackSuccessAsync(LlmUsageSuccessDto dto)
    {
        LlmUsageEvent? llmUsageEvent = null;

        try
        {
            if (string.IsNullOrWhiteSpace(dto.Provider) ||
                string.IsNullOrWhiteSpace(dto.Model) ||
                string.IsNullOrWhiteSpace(dto.OperationName))
            {
                _logger.LogWarning(
                    "LLM usage event skipped because required fields are empty. Provider: {Provider}, Model: {Model}, OperationName: {OperationName}",
                    dto.Provider,
                    dto.Model,
                    dto.OperationName);
                return null;
            }

            llmUsageEvent = new LlmUsageEvent
            {
                Id = Guid.NewGuid(),
                UserId = dto.UserId,
                CampaignId = dto.CampaignId,
                KeywordId = dto.KeywordId,
                Provider = Truncate(dto.Provider, 80)!,
                Model = Truncate(dto.Model, 120)!,
                OperationName = Truncate(dto.OperationName, 160)!,
                PromptTokens = dto.PromptTokens,
                CompletionTokens = dto.CompletionTokens,
                TotalTokens = dto.TotalTokens,
                BothubCaps = dto.BothubCaps,
                EstimatedCostRub = dto.EstimatedCostRub,
                IsSuccess = true,
                HttpStatusCode = dto.HttpStatusCode,
                DurationMs = dto.DurationMs,
                RequestMetadataJson = SerializeMetadata(dto.RequestMetadata),
                ResponseMetadataJson = SerializeMetadata(dto.ResponseMetadata),
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.LlmUsageEvents.Add(llmUsageEvent);
            await _dbContext.SaveChangesAsync();

            return llmUsageEvent.Id;
        }
        catch (Exception ex)
        {
            if (llmUsageEvent != null)
            {
                _dbContext.Entry(llmUsageEvent).State = EntityState.Detached;
            }

            _logger.LogError(
                ex,
                "Failed to save LLM usage event. Provider: {Provider}, Model: {Model}, OperationName: {OperationName}, UserId: {UserId}, CampaignId: {CampaignId}",
                dto.Provider,
                dto.Model,
                dto.OperationName,
                dto.UserId,
                dto.CampaignId);
            return null;
        }
    }

    public async Task<Guid?> TrackFailureAsync(LlmUsageFailureDto dto)
    {
        LlmUsageEvent? llmUsageEvent = null;

        try
        {
            if (string.IsNullOrWhiteSpace(dto.Provider) ||
                string.IsNullOrWhiteSpace(dto.Model) ||
                string.IsNullOrWhiteSpace(dto.OperationName))
            {
                _logger.LogWarning(
                    "Failed LLM usage event skipped because required fields are empty. Provider: {Provider}, Model: {Model}, OperationName: {OperationName}",
                    dto.Provider,
                    dto.Model,
                    dto.OperationName);
                return null;
            }

            llmUsageEvent = new LlmUsageEvent
            {
                Id = Guid.NewGuid(),
                UserId = dto.UserId,
                CampaignId = dto.CampaignId,
                KeywordId = dto.KeywordId,
                Provider = Truncate(dto.Provider, 80)!,
                Model = Truncate(dto.Model, 120)!,
                OperationName = Truncate(dto.OperationName, 160)!,
                IsSuccess = false,
                HttpStatusCode = dto.HttpStatusCode,
                ErrorCode = Truncate(dto.ErrorCode, 120),
                ErrorMessage = Truncate(dto.ErrorMessage, 1000),
                DurationMs = dto.DurationMs,
                RequestMetadataJson = SerializeMetadata(dto.RequestMetadata),
                ResponseMetadataJson = SerializeMetadata(dto.ResponseMetadata),
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.LlmUsageEvents.Add(llmUsageEvent);
            await _dbContext.SaveChangesAsync();

            return llmUsageEvent.Id;
        }
        catch (Exception ex)
        {
            if (llmUsageEvent != null)
            {
                _dbContext.Entry(llmUsageEvent).State = EntityState.Detached;
            }

            _logger.LogError(
                ex,
                "Failed to save failed LLM usage event. Provider: {Provider}, Model: {Model}, OperationName: {OperationName}, UserId: {UserId}, CampaignId: {CampaignId}",
                dto.Provider,
                dto.Model,
                dto.OperationName,
                dto.UserId,
                dto.CampaignId);
            return null;
        }
    }

    private static string? SerializeMetadata(object? metadata)
    {
        return metadata == null ? null : JsonSerializer.Serialize(metadata, JsonOptions);
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
