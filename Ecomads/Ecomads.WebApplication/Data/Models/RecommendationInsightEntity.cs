using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Ecomads.WebApplication.Models.Recommendations;

namespace Ecomads.WebApplication.Data.Models;

public class RecommendationInsightEntity
{
    [Key]
    public string Id { get; set; } = string.Empty;

    public Guid RecommendationRunId { get; set; }

    [ForeignKey(nameof(RecommendationRunId))]
    public Recommendation RecommendationRun { get; set; } = null!;

    public Guid CampaignId { get; set; }
    public DateTime PeriodFrom { get; set; }
    public DateTime PeriodTo { get; set; }
    public InsightEntityType EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public InsightType InsightType { get; set; }
    public KeywordRecommendationStatus Status { get; set; } = KeywordRecommendationStatus.Neutral;
    public double PriorityScore { get; set; }
    public PriorityLevel PriorityLevel { get; set; }
    public ConfidenceLevel ConfidenceLevel { get; set; }
    public RecommendationAction? RecommendedAction { get; set; }
    public InsightDecisionStatus DecisionStatus { get; set; } = InsightDecisionStatus.None;
    public string? UserComment { get; set; }
    public ExpectedEffectType ExpectedEffectType { get; set; } = ExpectedEffectType.NotCalculated;
    public decimal? ExpectedEffectMoney { get; set; }
    public string ExpectedEffectText { get; set; } = string.Empty;
    public decimal? ActualEffectMoney { get; set; }
    public string ActualEffectStatus { get; set; } = "WaitingForNextStats";
    public string MetricsJson { get; set; } = "{}";
    public string ReasonCodesJson { get; set; } = "[]";
    public string AllowedActionsJson { get; set; } = "[]";
    public string ForbiddenActionsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
