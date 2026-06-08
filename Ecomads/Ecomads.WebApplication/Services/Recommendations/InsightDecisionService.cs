using Ecomads.WebApplication.Data;
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

    public InsightDecisionService(EcomadsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<InsightDecisionUpdateDto?> UpdateDecisionAsync(
        string insightId,
        InsightDecisionStatus decisionStatus,
        CancellationToken cancellationToken = default)
    {
        if (decisionStatus == InsightDecisionStatus.None)
        {
            throw new ArgumentException(
                "Decision status must be Accepted, Postponed, Rejected or Applied.",
                nameof(decisionStatus));
        }

        var insight = await FindInsightAsync(insightId, cancellationToken);
        if (insight == null)
        {
            return null;
        }

        insight.DecisionStatus = decisionStatus;
        insight.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToUpdateDto(insight);
    }

    public async Task<InsightDecisionUpdateDto?> UpdateCommentAsync(
        string insightId,
        string? userComment,
        CancellationToken cancellationToken = default)
    {
        var insight = await FindInsightAsync(insightId, cancellationToken);
        if (insight == null)
        {
            return null;
        }

        insight.UserComment = string.IsNullOrWhiteSpace(userComment)
            ? null
            : userComment.Trim();
        insight.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToUpdateDto(insight);
    }

    private async Task<Data.Models.RecommendationInsightEntity?> FindInsightAsync(
        string insightId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(insightId))
        {
            return null;
        }

        return await _dbContext.RecommendationInsights
            .FirstOrDefaultAsync(insight => insight.Id == insightId, cancellationToken);
    }

    private static InsightDecisionUpdateDto ToUpdateDto(Data.Models.RecommendationInsightEntity insight)
    {
        return new InsightDecisionUpdateDto
        {
            RecommendationId = insight.RecommendationRunId,
            InsightId = insight.Id,
            DecisionStatus = insight.DecisionStatus,
            UserComment = insight.UserComment,
            UpdatedAt = insight.UpdatedAt,
            History = []
        };
    }
}
