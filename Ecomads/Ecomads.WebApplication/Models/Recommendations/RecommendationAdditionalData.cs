namespace Ecomads.WebApplication.Models.Recommendations;

public sealed class RecommendationAdditionalData
{
    public RecommendationGoal GoalType { get; init; }
    public decimal TargetDrr { get; init; }
    public List<RecommendationInsight> Insights { get; init; } = new();
    public List<RecommendationInsight> SelectedInsights { get; init; } = new();
    public Dictionary<string, InsightDecisionRecord> InsightDecisions { get; init; } = new();
    public string MetricsVersion { get; init; } = "recommendation-engine-mvp-v1";
    public bool GeneratedWithoutLlm { get; init; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class InsightDecisionRecord
{
    public InsightDecisionStatus DecisionStatus { get; init; } = InsightDecisionStatus.None;
    public string? UserComment { get; init; }
    public DateTime UpdatedAt { get; init; }
    public List<InsightDecisionHistoryItem> History { get; init; } = new();
}

public sealed class InsightDecisionHistoryItem
{
    public string Type { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public string? Comment { get; init; }
}
