namespace Ecomads.WebApplication.Models.Recommendations;

public sealed class KeywordRecommendationOverlayDto
{
    public Guid CampaignId { get; init; }
    public DateTime? GeneratedAt { get; init; }
    public KeywordRecommendationSummaryDto Summary { get; init; } = new();
    public RecommendationOverlaySummaryDto RecommendationSummary { get; init; } = new();
    public List<KeywordRecommendationRowDto> Keywords { get; init; } = new();
    public List<KeywordRecommendationInsightDetailDto> Insights { get; init; } = new();
    public List<CampaignRecommendationInsightDto> CampaignInsights { get; init; } = new();
}

public sealed class KeywordRecommendationSummaryDto
{
    public decimal Earned { get; init; }
    public decimal Spend { get; init; }
    public int Orders { get; init; }
    public decimal? Drr { get; init; }
    public decimal? Ctr { get; init; }
}

public sealed class RecommendationOverlaySummaryDto
{
    public string Text { get; init; } = string.Empty;
    public bool GeneratedWithoutLlm { get; init; }
    public RecommendationStatusCountsDto Counts { get; init; } = new();
}

public sealed class RecommendationStatusCountsDto
{
    public int ToRemove { get; init; }
    public int NeedsAttention { get; init; }
    public int Effective { get; init; }
    public int Watch { get; init; }
    public int LowData { get; init; }
    public int Neutral { get; init; }
}

public sealed class KeywordRecommendationRowDto
{
    public Guid KeywordId { get; init; }
    public string Phrase { get; init; } = string.Empty;
    public int? Views { get; init; }
    public int? Clicks { get; init; }
    public decimal? Ctr { get; init; }
    public decimal? Spend { get; init; }
    public int? Orders { get; init; }
    public decimal? Revenue { get; init; }
    public decimal? Drr { get; init; }
    public KeywordRecommendationStatus Status { get; init; } = KeywordRecommendationStatus.Neutral;
    public double PriorityScore { get; init; }
    public PriorityLevel PriorityLevel { get; init; } = PriorityLevel.Low;
    public ConfidenceLevel ConfidenceLevel { get; init; } = ConfidenceLevel.Low;
    public string? ShortRecommendation { get; init; }
    public RecommendationAction? RecommendedAction { get; init; }
    public string? MainInsightId { get; init; }
    public bool HasInsight { get; init; }
    public InsightDecisionStatus DecisionStatus { get; init; } = InsightDecisionStatus.None;
}

public sealed class KeywordRecommendationInsightDetailDto
{
    public string InsightId { get; init; } = string.Empty;
    public Guid KeywordId { get; init; }
    public string Phrase { get; init; } = string.Empty;
    public KeywordRecommendationStatus Status { get; init; }
    public double PriorityScore { get; init; }
    public PriorityLevel PriorityLevel { get; init; }
    public ConfidenceLevel ConfidenceLevel { get; init; }
    public Dictionary<string, decimal?> Metrics { get; init; } = new();
    public List<string> ReasonCodes { get; init; } = new();
    public string ShortExplanation { get; init; } = string.Empty;
    public string ExpectedEffectText { get; init; } = string.Empty;
    public string RecommendedActionTitle { get; init; } = string.Empty;
    public string RecommendedActionDescription { get; init; } = string.Empty;
    public List<RecommendationAction> AllowedActions { get; init; } = new();
    public List<RecommendationAction> ForbiddenActions { get; init; } = new();
    public InsightDecisionStatus DecisionStatus { get; init; } = InsightDecisionStatus.None;
    public string? UserComment { get; init; }
    public List<InsightHistoryItemDto> History { get; init; } = new();
}

public sealed class CampaignRecommendationInsightDto
{
    public string InsightId { get; init; } = string.Empty;
    public InsightType Type { get; init; }
    public double PriorityScore { get; init; }
    public PriorityLevel PriorityLevel { get; init; }
    public string Text { get; init; } = string.Empty;
    public InsightDecisionStatus DecisionStatus { get; init; } = InsightDecisionStatus.None;
    public string? UserComment { get; init; }
    public List<InsightHistoryItemDto> History { get; init; } = new();
}

public sealed class InsightHistoryItemDto
{
    public string Type { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public string? Comment { get; init; }
}

public sealed class UpdateInsightCommentRequest
{
    public string? UserComment { get; init; }
}

public sealed class InsightDecisionUpdateDto
{
    public Guid RecommendationId { get; init; }
    public string InsightId { get; init; } = string.Empty;
    public InsightDecisionStatus DecisionStatus { get; init; } = InsightDecisionStatus.None;
    public string? UserComment { get; init; }
    public DateTime UpdatedAt { get; init; }
    public List<InsightHistoryItemDto> History { get; init; } = new();
}
