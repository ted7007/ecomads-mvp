namespace Ecomads.WebApplication.Models.Recommendations;

public sealed class RecommendationInsight
{
    public string Id { get; init; } = string.Empty;
    public InsightType Type { get; init; }
    public InsightEntityType EntityType { get; init; }
    public string EntityName { get; init; } = string.Empty;

    public double PriorityScore { get; init; }
    public PriorityLevel PriorityLevel { get; init; }

    public double ImpactScore { get; init; }
    public double UrgencyScore { get; init; }
    public double ConfidenceScore { get; init; }
    public ConfidenceLevel ConfidenceLevel { get; init; }

    public Dictionary<string, decimal?> Metrics { get; init; } = new();
    public List<RecommendationAction> AllowedActions { get; init; } = new();
    public List<RecommendationAction> ForbiddenActions { get; init; } = new();
    public List<string> ReasonCodes { get; init; } = new();

    public string? TechnicalComment { get; init; }
}
