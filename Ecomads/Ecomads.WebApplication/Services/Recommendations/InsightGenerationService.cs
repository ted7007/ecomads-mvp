using Ecomads.WebApplication.Models.Recommendations;
using Microsoft.Extensions.Options;

namespace Ecomads.WebApplication.Services.Recommendations;

public interface IInsightGenerationService
{
    IReadOnlyList<RecommendationInsight> GenerateInsights(RecommendationGenerationContext context);
}

public sealed class InsightGenerationService : IInsightGenerationService
{
    private readonly RecommendationEngineOptions _options;

    public InsightGenerationService(IOptions<RecommendationEngineOptions> options)
    {
        _options = options.Value;
    }

    public IReadOnlyList<RecommendationInsight> GenerateInsights(RecommendationGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var insights = new List<RecommendationInsight>();

        foreach (var metrics in context.KeywordMetrics)
        {
            var confidenceLevel = GetConfidenceLevel(metrics);
            var confidenceScore = GetConfidenceScore(confidenceLevel);

            if (IsLowData(metrics))
            {
                insights.Add(CreateKeywordInsight(
                    InsightType.LowData,
                    metrics,
                    confidenceLevel,
                    confidenceScore,
                    ["low_confidence", "not_enough_clicks", "not_enough_spend"],
                    "Недостаточно данных для уверенного вывода по ключевому запросу."));
                continue;
            }

            if (IsBadSpendWithoutOrders(metrics))
            {
                insights.Add(CreateKeywordInsight(
                    InsightType.BadSpendWithoutOrders,
                    metrics,
                    confidenceLevel,
                    confidenceScore,
                    ["significant_spend_without_orders"],
                    "Ключевой запрос тратит значимый бюджет, но не приносит заказов."));
                continue;
            }

            if (IsScaleCandidate(metrics, confidenceLevel, context.TargetDrr))
            {
                insights.Add(CreateKeywordInsight(
                    InsightType.ScaleCandidate,
                    metrics,
                    confidenceLevel,
                    confidenceScore,
                    ["drr_below_target", "has_stable_orders", "growth_candidate", "keyword_converts"],
                    "Ключевой запрос приносит заказы с приемлемой экономикой."));
                continue;
            }

            if (IsWatchCandidate(metrics, confidenceLevel, context.TargetDrr))
            {
                insights.Add(CreateKeywordInsight(
                    InsightType.WatchCandidate,
                    metrics,
                    confidenceLevel,
                    confidenceScore,
                    GetWatchCandidateReasonCodes(metrics, confidenceLevel, context.TargetDrr),
                    "Ключевой запрос неоднозначный и требует наблюдения."));
                continue;
            }

            if (IsBadDrr(metrics, confidenceLevel, context.TargetDrr))
            {
                insights.Add(CreateKeywordInsight(
                    InsightType.BadDrr,
                    metrics,
                    confidenceLevel,
                    confidenceScore,
                    ["drr_above_target"],
                    "Ключевой запрос приносит заказы, но ДРР выше целевого значения."));
            }
        }

        return insights;
    }

    private bool IsLowData(CalculatedKeywordMetrics metrics)
    {
        return (metrics.Clicks ?? 0) < _options.MinClicksForConclusion
            && (metrics.Spend ?? 0m) < _options.MinSpendForConclusion
            && (metrics.Orders ?? 0) < _options.MinOrdersForPositiveConclusion;
    }

    private bool IsBadSpendWithoutOrders(CalculatedKeywordMetrics metrics)
    {
        return (metrics.Spend ?? 0m) >= _options.MinSpendForConclusion
            && (metrics.Orders ?? 0) == 0
            && (metrics.Clicks ?? 0) >= _options.MinClicksForConclusion;
    }

    private bool IsBadDrr(
        CalculatedKeywordMetrics metrics,
        ConfidenceLevel confidenceLevel,
        decimal targetDrr)
    {
        return (metrics.Orders ?? 0) > 0
            && metrics.Drr.HasValue
            && metrics.Drr.Value > targetDrr
            && metrics.Drr.Value > targetDrr * 1.2m
            && (metrics.Spend ?? 0m) >= _options.MinSpendForConclusion
            && confidenceLevel != ConfidenceLevel.Low;
    }

    private bool IsScaleCandidate(
        CalculatedKeywordMetrics metrics,
        ConfidenceLevel confidenceLevel,
        decimal targetDrr)
    {
        return (metrics.Orders ?? 0) >= _options.MinOrdersForPositiveConclusion
            && metrics.Drr.HasValue
            && metrics.Drr.Value <= targetDrr
            && confidenceLevel != ConfidenceLevel.Low;
    }

    private static bool IsWatchCandidate(
        CalculatedKeywordMetrics metrics,
        ConfidenceLevel confidenceLevel,
        decimal targetDrr)
    {
        return confidenceLevel == ConfidenceLevel.Low
            || ((metrics.Orders ?? 0) > 0
                && metrics.Drr.HasValue
                && metrics.Drr.Value > targetDrr
                && metrics.Drr.Value <= targetDrr * 1.2m);
    }

    private ConfidenceLevel GetConfidenceLevel(CalculatedKeywordMetrics metrics)
    {
        if ((metrics.Clicks ?? 0) >= 100
            || (metrics.Spend ?? 0m) >= 3000m
            || (metrics.Orders ?? 0) >= 10)
        {
            return ConfidenceLevel.High;
        }

        if ((metrics.Clicks ?? 0) >= _options.MinClicksForConclusion
            || (metrics.Spend ?? 0m) >= _options.MinSpendForConclusion
            || (metrics.Orders ?? 0) >= _options.MinOrdersForPositiveConclusion)
        {
            return ConfidenceLevel.Medium;
        }

        return ConfidenceLevel.Low;
    }

    private static double GetConfidenceScore(ConfidenceLevel confidenceLevel)
    {
        return confidenceLevel switch
        {
            ConfidenceLevel.High => 1.0,
            ConfidenceLevel.Medium => 0.7,
            _ => 0.4
        };
    }

    private static IReadOnlyCollection<string> GetWatchCandidateReasonCodes(
        CalculatedKeywordMetrics metrics,
        ConfidenceLevel confidenceLevel,
        decimal targetDrr)
    {
        var reasonCodes = new List<string> { "watch_candidate" };

        if (confidenceLevel == ConfidenceLevel.Low)
        {
            reasonCodes.Add("low_confidence");
        }

        if ((metrics.Orders ?? 0) > 0
            && metrics.Drr.HasValue
            && metrics.Drr.Value > targetDrr
            && metrics.Drr.Value <= targetDrr * 1.2m)
        {
            reasonCodes.Add("small_drr_deviation");
        }

        return reasonCodes;
    }

    private static RecommendationInsight CreateKeywordInsight(
        InsightType type,
        CalculatedKeywordMetrics metrics,
        ConfidenceLevel confidenceLevel,
        double confidenceScore,
        IReadOnlyCollection<string> reasonCodes,
        string technicalComment)
    {
        return new RecommendationInsight
        {
            Id = $"keyword:{metrics.KeywordStatisticId}:{type}",
            Type = type,
            EntityType = InsightEntityType.Keyword,
            EntityName = metrics.Phrase,
            ConfidenceLevel = confidenceLevel,
            ConfidenceScore = confidenceScore,
            Metrics = BuildMetricsDictionary(metrics),
            ReasonCodes = reasonCodes.ToList(),
            TechnicalComment = technicalComment
        };
    }

    private static Dictionary<string, decimal?> BuildMetricsDictionary(CalculatedKeywordMetrics metrics)
    {
        return new Dictionary<string, decimal?>
        {
            ["spend"] = metrics.Spend,
            ["revenue"] = metrics.Revenue,
            ["orders"] = metrics.Orders,
            ["clicks"] = metrics.Clicks,
            ["impressions"] = metrics.Impressions,
            ["drr"] = metrics.Drr,
            ["ctr"] = metrics.Ctr,
            ["cpc"] = metrics.Cpc,
            ["cr"] = metrics.Cr,
            ["cpo"] = metrics.Cpo,
            ["averageOrderValue"] = metrics.AverageOrderValue,
            ["avgDailyOrders"] = metrics.AvgDailyOrders
        };
    }
}
