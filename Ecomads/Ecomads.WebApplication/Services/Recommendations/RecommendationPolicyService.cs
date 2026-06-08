using Ecomads.WebApplication.Models.Recommendations;
using Microsoft.Extensions.Options;

namespace Ecomads.WebApplication.Services.Recommendations;

public interface IRecommendationPolicyService
{
    IReadOnlyList<RecommendationInsight> ApplyPolicies(
        RecommendationGenerationContext context,
        IReadOnlyCollection<RecommendationInsight> insights);
}

public sealed class RecommendationPolicyService : IRecommendationPolicyService
{
    private readonly RecommendationEngineOptions _options;

    public RecommendationPolicyService(IOptions<RecommendationEngineOptions> options)
    {
        _options = options.Value;
    }

    public IReadOnlyList<RecommendationInsight> ApplyPolicies(
        RecommendationGenerationContext context,
        IReadOnlyCollection<RecommendationInsight> insights)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(insights);

        return insights
            .Select(insight => ApplyPolicy(context, insight))
            .ToList();
    }

    private RecommendationInsight ApplyPolicy(
        RecommendationGenerationContext context,
        RecommendationInsight insight)
    {
        var allowedActions = new List<RecommendationAction>(insight.AllowedActions);
        var forbiddenActions = new List<RecommendationAction>(insight.ForbiddenActions);

        AddTypeActions(insight, allowedActions, forbiddenActions);
        AddGuardrailActions(context, insight, forbiddenActions);

        return CopyInsight(
            insight,
            DistinctActions(allowedActions),
            DistinctActions(forbiddenActions));
    }

    private void AddTypeActions(
        RecommendationInsight insight,
        List<RecommendationAction> allowedActions,
        List<RecommendationAction> forbiddenActions)
    {
        switch (insight.Type)
        {
            case InsightType.LowData:
                allowedActions.AddRange([RecommendationAction.CollectMoreData, RecommendationAction.Watch]);
                forbiddenActions.AddRange([
                    RecommendationAction.MinusKeyword,
                    RecommendationAction.Scale,
                    RecommendationAction.AggressiveBidChange,
                    RecommendationAction.ImmediateMinusKeyword
                ]);
                break;

            case InsightType.BadSpendWithoutOrders:
                allowedActions.AddRange([
                    RecommendationAction.DecreaseBid,
                    RecommendationAction.ConsiderMinusKeyword,
                    RecommendationAction.MoveToWatchlist
                ]);
                forbiddenActions.AddRange([RecommendationAction.IncreaseBid, RecommendationAction.Scale]);
                break;

            case InsightType.SemanticIrrelevant:
                allowedActions.AddRange([
                    RecommendationAction.ConsiderMinusKeyword,
                    RecommendationAction.MinusKeyword,
                    RecommendationAction.MoveToWatchlist
                ]);
                forbiddenActions.AddRange([
                    RecommendationAction.IncreaseBid,
                    RecommendationAction.Scale,
                    RecommendationAction.AggressiveScale
                ]);
                break;

            case InsightType.BadDrr:
                allowedActions.AddRange([
                    RecommendationAction.DecreaseBid,
                    RecommendationAction.Watch,
                    RecommendationAction.Optimize
                ]);

                if (GetMetric(insight, "drr") > _options.TargetDrr * 1.5m
                    && (GetMetric(insight, "orders") ?? 0m) < _options.MinOrdersForPositiveConclusion)
                {
                    allowedActions.Add(RecommendationAction.ConsiderMinusKeyword);
                }

                forbiddenActions.AddRange([RecommendationAction.IncreaseBid, RecommendationAction.Scale]);
                break;

            case InsightType.ScaleCandidate:
                allowedActions.AddRange([
                    RecommendationAction.IncreaseBidGradually,
                    RecommendationAction.Scale,
                    RecommendationAction.FindSimilarKeywords,
                    RecommendationAction.Maintain
                ]);
                forbiddenActions.AddRange([
                    RecommendationAction.MinusKeyword,
                    RecommendationAction.Disable,
                    RecommendationAction.ImmediateMinusKeyword,
                    RecommendationAction.ImmediateDisable
                ]);
                break;

            case InsightType.WatchCandidate:
                allowedActions.AddRange([
                    RecommendationAction.Watch,
                    RecommendationAction.CollectMoreData,
                    RecommendationAction.DecreaseBidCarefully
                ]);
                forbiddenActions.AddRange([
                    RecommendationAction.AggressiveScale,
                    RecommendationAction.ImmediateDisable
                ]);
                break;
        }
    }

    private static void AddGuardrailActions(
        RecommendationGenerationContext context,
        RecommendationInsight insight,
        List<RecommendationAction> forbiddenActions)
    {
        var orders = GetMetric(insight, "orders");
        var drr = GetMetric(insight, "drr");

        if (orders > 0m
            && drr.HasValue
            && drr.Value <= context.TargetDrr * 1.2m)
        {
            forbiddenActions.Add(RecommendationAction.ImmediateMinusKeyword);
        }

        if (drr.HasValue && drr.Value > context.TargetDrr * 1.5m)
        {
            forbiddenActions.Add(RecommendationAction.Scale);
            forbiddenActions.Add(RecommendationAction.AggressiveScale);
        }

        if (insight.ConfidenceLevel == ConfidenceLevel.Low)
        {
            forbiddenActions.Add(RecommendationAction.ImmediateMinusKeyword);
            forbiddenActions.Add(RecommendationAction.AggressiveBidChange);
            forbiddenActions.Add(RecommendationAction.Scale);
            forbiddenActions.Add(RecommendationAction.AggressiveScale);
        }

        if (context.Goal == RecommendationGoal.SellOutStock
            && insight.Type == InsightType.StockRisk
            && insight.PriorityLevel >= PriorityLevel.High)
        {
            forbiddenActions.Add(RecommendationAction.AggressivelyReduceAllSpend);
        }
    }

    private static decimal? GetMetric(RecommendationInsight insight, string name)
    {
        return insight.Metrics.TryGetValue(name, out var value) ? value : null;
    }

    private static List<RecommendationAction> DistinctActions(IEnumerable<RecommendationAction> actions)
    {
        return actions.Distinct().ToList();
    }

    private static RecommendationInsight CopyInsight(
        RecommendationInsight insight,
        List<RecommendationAction> allowedActions,
        List<RecommendationAction> forbiddenActions)
    {
        return new RecommendationInsight
        {
            Id = insight.Id,
            Type = insight.Type,
            EntityType = insight.EntityType,
            EntityName = insight.EntityName,
            PriorityScore = insight.PriorityScore,
            PriorityLevel = insight.PriorityLevel,
            ImpactScore = insight.ImpactScore,
            UrgencyScore = insight.UrgencyScore,
            ConfidenceScore = insight.ConfidenceScore,
            ConfidenceLevel = insight.ConfidenceLevel,
            Metrics = new Dictionary<string, decimal?>(insight.Metrics),
            AllowedActions = allowedActions,
            ForbiddenActions = forbiddenActions,
            ReasonCodes = insight.ReasonCodes.ToList(),
            TechnicalComment = insight.TechnicalComment
        };
    }
}
