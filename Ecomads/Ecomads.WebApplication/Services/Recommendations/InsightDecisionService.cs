using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Models.Recommendations;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Recommendations;

public interface IInsightDecisionService
{
    Task<InsightDecisionUpdateDto?> UpdateDecisionAsync(
        string insightId,
        InsightDecisionStatus decisionStatus,
        CancellationToken cancellationToken = default);

    Task<InsightDecisionUpdateDto?> UpdateCommentAsync(
        string insightId,
        string? userComment,
        CancellationToken cancellationToken = default);
}

public sealed class InsightDecisionService : IInsightDecisionService
{
    private readonly EcomadsDbContext _dbContext;
    private readonly ILogger<InsightDecisionService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public InsightDecisionService(
        EcomadsDbContext dbContext,
        ILogger<InsightDecisionService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<InsightDecisionUpdateDto?> UpdateDecisionAsync(
        string insightId,
        InsightDecisionStatus decisionStatus,
        CancellationToken cancellationToken = default)
    {
        if (decisionStatus == InsightDecisionStatus.None)
        {
            throw new ArgumentException("Decision status must be Accepted, Postponed or Rejected.", nameof(decisionStatus));
        }

        var match = await FindLatestRecommendationWithInsightAsync(insightId, cancellationToken);
        if (match == null)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var previous = GetDecision(match.AdditionalData, insightId);
        var history = previous?.History.ToList() ?? new List<InsightDecisionHistoryItem>();
        history.Add(new InsightDecisionHistoryItem
        {
            Type = decisionStatus.ToString(),
            CreatedAt = now,
            Comment = previous?.UserComment
        });

        var updated = new InsightDecisionRecord
        {
            DecisionStatus = decisionStatus,
            UserComment = previous?.UserComment,
            UpdatedAt = now,
            History = history
        };

        return await SaveDecisionAsync(match.Recommendation, match.AdditionalData, insightId, updated, cancellationToken);
    }

    public async Task<InsightDecisionUpdateDto?> UpdateCommentAsync(
        string insightId,
        string? userComment,
        CancellationToken cancellationToken = default)
    {
        var match = await FindLatestRecommendationWithInsightAsync(insightId, cancellationToken);
        if (match == null)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var trimmedComment = string.IsNullOrWhiteSpace(userComment)
            ? null
            : userComment.Trim();
        var previous = GetDecision(match.AdditionalData, insightId);
        var history = previous?.History.ToList() ?? new List<InsightDecisionHistoryItem>();
        history.Add(new InsightDecisionHistoryItem
        {
            Type = "CommentUpdated",
            CreatedAt = now,
            Comment = trimmedComment
        });

        var updated = new InsightDecisionRecord
        {
            DecisionStatus = previous?.DecisionStatus ?? InsightDecisionStatus.None,
            UserComment = trimmedComment,
            UpdatedAt = now,
            History = history
        };

        return await SaveDecisionAsync(match.Recommendation, match.AdditionalData, insightId, updated, cancellationToken);
    }

    private async Task<RecommendationMatch?> FindLatestRecommendationWithInsightAsync(
        string insightId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(insightId))
        {
            return null;
        }

        var recommendations = await _dbContext.Recommendations
            .OrderByDescending(recommendation => recommendation.CreatedAt)
            .ToListAsync(cancellationToken);

        foreach (var recommendation in recommendations)
        {
            var additionalData = DeserializeAdditionalData(recommendation);
            if (additionalData == null)
            {
                continue;
            }

            if (ContainsInsight(additionalData, insightId))
            {
                return new RecommendationMatch(recommendation, additionalData);
            }
        }

        return null;
    }

    private RecommendationAdditionalData? DeserializeAdditionalData(Recommendation recommendation)
    {
        if (string.IsNullOrWhiteSpace(recommendation.AdditionalData))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RecommendationAdditionalData>(
                recommendation.AdditionalData,
                JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Не удалось прочитать AdditionalData рекомендации {RecommendationId}",
                recommendation.Id);
            return null;
        }
    }

    private static bool ContainsInsight(RecommendationAdditionalData additionalData, string insightId)
    {
        return additionalData.Insights.Any(insight => string.Equals(insight.Id, insightId, StringComparison.Ordinal))
            || additionalData.SelectedInsights.Any(insight => string.Equals(insight.Id, insightId, StringComparison.Ordinal));
    }

    private static InsightDecisionRecord? GetDecision(
        RecommendationAdditionalData additionalData,
        string insightId)
    {
        return additionalData.InsightDecisions.TryGetValue(insightId, out var decision)
            ? decision
            : null;
    }

    private async Task<InsightDecisionUpdateDto> SaveDecisionAsync(
        Recommendation recommendation,
        RecommendationAdditionalData additionalData,
        string insightId,
        InsightDecisionRecord updated,
        CancellationToken cancellationToken)
    {
        additionalData.InsightDecisions[insightId] = updated;
        recommendation.AdditionalData = JsonSerializer.Serialize(additionalData, JsonOptions);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new InsightDecisionUpdateDto
        {
            RecommendationId = recommendation.Id,
            InsightId = insightId,
            DecisionStatus = updated.DecisionStatus,
            UserComment = updated.UserComment,
            UpdatedAt = updated.UpdatedAt,
            History = updated.History
                .Select(ToHistoryDto)
                .ToList()
        };
    }

    private static InsightHistoryItemDto ToHistoryDto(InsightDecisionHistoryItem item)
    {
        return new InsightHistoryItemDto
        {
            Type = item.Type,
            CreatedAt = item.CreatedAt,
            Comment = item.Comment
        };
    }

    private sealed record RecommendationMatch(
        Recommendation Recommendation,
        RecommendationAdditionalData AdditionalData);
}
