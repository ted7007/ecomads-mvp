using Ecomads.WebApplication.Models.Recommendations;
using Microsoft.Extensions.Options;

namespace Ecomads.WebApplication.Services.Recommendations;

public interface IPriorityScoringService
{
    IReadOnlyList<RecommendationInsight> ScoreInsights(
        RecommendationGenerationContext context,
        IReadOnlyCollection<RecommendationInsight> insights);
}

public sealed class PriorityScoringService : IPriorityScoringService
{
    private readonly RecommendationEngineOptions _options;

    public PriorityScoringService(IOptions<RecommendationEngineOptions> options)
    {
        _options = options.Value;
    }

    public IReadOnlyList<RecommendationInsight> ScoreInsights(
        RecommendationGenerationContext context,
        IReadOnlyCollection<RecommendationInsight> insights)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(insights);

        return insights
            .Select(insight => ScoreInsight(context, insight))
            .ToList();
    }

    private RecommendationInsight ScoreInsight(
        RecommendationGenerationContext context,
        RecommendationInsight insight)
    {
        var impactScore = GetImpactScore(insight);
        var urgencyScore = GetUrgencyScore(insight);
        var seasonScore = 1.0;
        var stockRiskScore = 1.0;
        var goalWeight = GetGoalWeight(context.Goal, insight.Type);

        var rawPriorityScore = goalWeight
            * impactScore
            * urgencyScore
            * insight.ConfidenceScore
            * seasonScore
            * stockRiskScore;

        var priorityScore = Math.Min(
            100d,
            Math.Round(rawPriorityScore * _options.PriorityMultiplier, MidpointRounding.AwayFromZero));

        var priorityLevel = GetPriorityLevel(priorityScore);
        priorityLevel = ApplyPriorityLevelOverrides(insight, priorityLevel);

        return new RecommendationInsight
        {
            Id = insight.Id,
            Type = insight.Type,
            EntityType = insight.EntityType,
            EntityName = insight.EntityName,
            PriorityScore = priorityScore,
            PriorityLevel = priorityLevel,
            ImpactScore = impactScore,
            UrgencyScore = urgencyScore,
            ConfidenceScore = insight.ConfidenceScore,
            ConfidenceLevel = insight.ConfidenceLevel,
            Metrics = new Dictionary<string, decimal?>(insight.Metrics),
            AllowedActions = insight.AllowedActions.ToList(),
            ForbiddenActions = insight.ForbiddenActions.ToList(),
            ReasonCodes = insight.ReasonCodes.ToList(),
            TechnicalComment = insight.TechnicalComment
        };
    }

    private static double GetGoalWeight(RecommendationGoal goal, InsightType insightType)
    {
        return goal switch
        {
            RecommendationGoal.ReduceDrr => insightType switch
            {
                InsightType.BadSpendWithoutOrders => 1.5,
                InsightType.SemanticIrrelevant => 1.3,
                InsightType.BadDrr => 1.4,
                InsightType.CampaignEfficiencyProblem => 1.4,
                InsightType.WatchCandidate => 0.9,
                InsightType.ScaleCandidate => 0.8,
                InsightType.StockRisk => 0.7,
                InsightType.SeasonRisk => 0.7,
                _ => 1.0
            },
            RecommendationGoal.IncreaseOrders => insightType switch
            {
                InsightType.ScaleCandidate => 1.5,
                InsightType.PositionGrowthCandidate => 1.3,
                InsightType.GoodKeyword => 1.3,
                InsightType.StockRisk => 1.1,
                InsightType.BadSpendWithoutOrders => 1.0,
                InsightType.SemanticIrrelevant => 0.8,
                InsightType.BadDrr => 0.9,
                InsightType.LowData => 0.5,
                _ => 1.0
            },
            RecommendationGoal.SellOutStock => insightType switch
            {
                InsightType.StockRisk => 1.7,
                InsightType.SeasonRisk => 1.5,
                InsightType.ScaleCandidate => 1.4,
                InsightType.GoodKeyword => 1.3,
                InsightType.PositionGrowthCandidate => 1.2,
                InsightType.BadSpendWithoutOrders => 1.0,
                InsightType.SemanticIrrelevant => 0.8,
                InsightType.BadDrr => 0.8,
                InsightType.WatchCandidate => 0.8,
                InsightType.LowData => 0.5,
                _ => 1.0
            },
            RecommendationGoal.IncreaseRevenue => insightType switch
            {
                InsightType.ScaleCandidate => 1.5,
                InsightType.GoodKeyword => 1.4,
                InsightType.PositionGrowthCandidate => 1.3,
                InsightType.CampaignGrowthOpportunity => 1.3,
                InsightType.BadSpendWithoutOrders => 0.9,
                InsightType.SemanticIrrelevant => 0.8,
                InsightType.BadDrr => 0.9,
                InsightType.StockRisk => 1.0,
                _ => 1.0
            },
            RecommendationGoal.MaintainPosition => insightType switch
            {
                InsightType.PositionGrowthCandidate => 1.5,
                InsightType.GoodKeyword => 1.3,
                InsightType.ScaleCandidate => 1.2,
                InsightType.BadDrr => 0.9,
                InsightType.BadSpendWithoutOrders => 0.8,
                InsightType.SemanticIrrelevant => 0.8,
                InsightType.StockRisk => 0.8,
                _ => 1.0
            },
            _ => 1.0
        };
    }

    private static double GetImpactScore(RecommendationInsight insight)
    {
        if (insight.Type is InsightType.BadSpendWithoutOrders or InsightType.BadDrr)
        {
            return GetBadSpendImpactScore(GetMetric(insight, "spend") ?? 0m);
        }

        if (insight.Type == InsightType.SemanticIrrelevant)
        {
            return 0.7;
        }

        if (insight.Type == InsightType.ScaleCandidate)
        {
            return GetScaleImpactScore(GetMetric(insight, "orders") ?? 0m);
        }

        if (insight.Type == InsightType.StockRisk)
        {
            return GetStockRiskImpactScore(GetMetric(insight, "salesPaceCoverage"));
        }

        return 0.5;
    }

    private static double GetBadSpendImpactScore(decimal spend)
    {
        if (spend < 300m)
        {
            return 0.2;
        }

        if (spend < 1000m)
        {
            return 0.4;
        }

        if (spend < 3000m)
        {
            return 0.7;
        }

        return 1.0;
    }

    private static double GetScaleImpactScore(decimal orders)
    {
        if (orders < 3m)
        {
            return 0.3;
        }

        if (orders < 10m)
        {
            return 0.6;
        }

        if (orders < 30m)
        {
            return 0.8;
        }

        return 1.0;
    }

    private static double GetStockRiskImpactScore(decimal? salesPaceCoverage)
    {
        if (!salesPaceCoverage.HasValue)
        {
            return 1.0;
        }

        if (salesPaceCoverage.Value >= 1.0m)
        {
            return 0.2;
        }

        if (salesPaceCoverage.Value >= 0.7m)
        {
            return 0.5;
        }

        if (salesPaceCoverage.Value >= 0.4m)
        {
            return 0.8;
        }

        return 1.0;
    }

    private static double GetUrgencyScore(RecommendationInsight insight)
    {
        if (insight.Type == InsightType.BadSpendWithoutOrders
            && (GetMetric(insight, "spend") ?? 0m) >= 3000m
            && (GetMetric(insight, "orders") ?? 0m) == 0m)
        {
            return 1.5;
        }

        return 1.0;
    }

    private static PriorityLevel GetPriorityLevel(double priorityScore)
    {
        return priorityScore switch
        {
            < 30 => PriorityLevel.Low,
            < 60 => PriorityLevel.Medium,
            < 80 => PriorityLevel.High,
            _ => PriorityLevel.Critical
        };
    }

    private PriorityLevel ApplyPriorityLevelOverrides(
        RecommendationInsight insight,
        PriorityLevel priorityLevel)
    {
        if (insight.Type == InsightType.BadSpendWithoutOrders
            && (GetMetric(insight, "spend") ?? 0m) >= _options.MinSpendForConclusion * 2m
            && (GetMetric(insight, "clicks") ?? 0m) >= _options.MinClicksForConclusion
            && priorityLevel < PriorityLevel.High)
        {
            return PriorityLevel.High;
        }

        if (insight.Type == InsightType.BadSpendWithoutOrders
            && (GetMetric(insight, "spend") ?? 0m) >= _options.MinSpendForConclusion
            && (GetMetric(insight, "clicks") ?? 0m) >= _options.MinClicksForConclusion
            && priorityLevel < PriorityLevel.Medium)
        {
            return PriorityLevel.Medium;
        }

        if (insight.Type == InsightType.BadSpendWithoutOrders
            && (GetMetric(insight, "spend") ?? 0m) >= 3000m
            && insight.ConfidenceLevel == ConfidenceLevel.High
            && priorityLevel < PriorityLevel.High)
        {
            return PriorityLevel.High;
        }

        return priorityLevel;
    }

    private static decimal? GetMetric(RecommendationInsight insight, string name)
    {
        return insight.Metrics.TryGetValue(name, out var value) ? value : null;
    }
}
