namespace Ecomads.WebApplication.Models.Recommendations;

public sealed class RecommendationGenerationContext
{
    public Guid CampaignId { get; init; }
    public string CampaignName { get; init; } = string.Empty;
    public string OriginalGoal { get; init; } = string.Empty;
    public RecommendationGoal Goal { get; init; }
    public decimal TargetDrr { get; init; }

    public CalculatedCampaignMetrics? CampaignMetrics { get; init; }
    public List<CalculatedKeywordMetrics> KeywordMetrics { get; init; } = new();

    public int? Stock { get; init; }
    public int? DaysUntilDemandDrop { get; init; }
    public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;
}
