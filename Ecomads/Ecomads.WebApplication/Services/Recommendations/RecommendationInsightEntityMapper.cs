using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Models.Recommendations;
using Microsoft.Extensions.Options;

namespace Ecomads.WebApplication.Services.Recommendations;

public interface IRecommendationInsightEntityMapper
{
    RecommendationInsightEntity Map(
        Recommendation recommendationRun,
        RecommendationInsight insight,
        DateTime periodFrom,
        DateTime periodTo);
}

public sealed class RecommendationInsightEntityMapper : IRecommendationInsightEntityMapper
{
    private readonly RecommendationEngineOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public RecommendationInsightEntityMapper(IOptions<RecommendationEngineOptions> options)
    {
        _options = options.Value;
    }

    public RecommendationInsightEntity Map(
        Recommendation recommendationRun,
        RecommendationInsight insight,
        DateTime periodFrom,
        DateTime periodTo)
    {
        var status = MapStatus(insight.Type);
        var recommendedAction = GetRecommendedAction(insight, status);
        var expectedEffect = CalculateExpectedEffect(insight, status);
        var now = recommendationRun.CreatedAt == default ? DateTime.UtcNow : recommendationRun.CreatedAt;

        return new RecommendationInsightEntity
        {
            Id = $"{recommendationRun.Id:N}:{insight.Id}",
            RecommendationRunId = recommendationRun.Id,
            CampaignId = recommendationRun.CampaignId,
            PeriodFrom = periodFrom,
            PeriodTo = periodTo,
            EntityType = insight.EntityType,
            EntityId = TryGetEntityId(insight),
            EntityName = insight.EntityName,
            InsightType = insight.Type,
            Status = status,
            PriorityScore = insight.PriorityScore,
            PriorityLevel = insight.PriorityLevel,
            ConfidenceLevel = insight.ConfidenceLevel,
            RecommendedAction = recommendedAction,
            DecisionStatus = InsightDecisionStatus.None,
            ExpectedEffectType = expectedEffect.Type,
            ExpectedEffectMoney = expectedEffect.Money,
            ExpectedEffectText = expectedEffect.Text,
            MetricsJson = JsonSerializer.Serialize(insight.Metrics, JsonOptions),
            ReasonCodesJson = JsonSerializer.Serialize(insight.ReasonCodes, JsonOptions),
            AllowedActionsJson = JsonSerializer.Serialize(insight.AllowedActions, JsonOptions),
            ForbiddenActionsJson = JsonSerializer.Serialize(insight.ForbiddenActions, JsonOptions),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public static KeywordRecommendationStatus MapStatus(InsightType insightType)
    {
        return insightType switch
        {
            InsightType.BadSpendWithoutOrders => KeywordRecommendationStatus.ToRemove,
            InsightType.SemanticIrrelevant => KeywordRecommendationStatus.ToRemove,
            InsightType.BadDrr => KeywordRecommendationStatus.NeedsAttention,
            InsightType.ScaleCandidate => KeywordRecommendationStatus.Effective,
            InsightType.GoodKeyword => KeywordRecommendationStatus.Effective,
            InsightType.WatchCandidate => KeywordRecommendationStatus.Watch,
            InsightType.LowData => KeywordRecommendationStatus.LowData,
            InsightType.IrrelevantButConverting => KeywordRecommendationStatus.Watch,
            InsightType.PositionGrowthCandidate => KeywordRecommendationStatus.Effective,
            _ => KeywordRecommendationStatus.Neutral
        };
    }

    private ExpectedEffectResult CalculateExpectedEffect(
        RecommendationInsight insight,
        KeywordRecommendationStatus status)
    {
        var spend = GetMetric(insight, "spend") ?? 0m;
        var drr = GetMetric(insight, "drr");
        var orders = GetMetric(insight, "orders") ?? 0m;

        if (insight.Type == InsightType.BadSpendWithoutOrders || status == KeywordRecommendationStatus.ToRemove)
        {
            if (spend <= 0m)
            {
                return NotCalculated("Ожидаемый эффект не рассчитывается: нет расхода за период.");
            }

            return new ExpectedEffectResult(
                ExpectedEffectType.Saving,
                spend,
                $"Потенциальная экономия до {FormatMoney(spend)} за аналогичный период.");
        }

        if (insight.Type == InsightType.BadDrr || status == KeywordRecommendationStatus.NeedsAttention)
        {
            if (spend <= 0m)
            {
                return NotCalculated("Ожидаемый эффект не рассчитывается: нет расхода за период.");
            }

            var saving = Math.Round(spend * _options.BadDrrSpendReductionCoefficient, 2, MidpointRounding.AwayFromZero);
            return new ExpectedEffectResult(
                ExpectedEffectType.Saving,
                saving,
                $"Оценочная экономия около {FormatMoney(saving)} при снижении расхода примерно на 30%.");
        }

        if (insight.Type == InsightType.ScaleCandidate || status == KeywordRecommendationStatus.Effective)
        {
            if (!drr.HasValue || drr.Value <= 0m || drr.Value > _options.TargetDrr || orders <= 0m || spend <= 0m)
            {
                return NotCalculated("Ожидаемый эффект не рассчитывается: недостаточно условий для оценки масштабирования.");
            }

            var additionalSpend = spend * _options.ScaleCandidateSpendIncreaseCoefficient;
            var additionalRevenue = Math.Round(additionalSpend / (drr.Value / 100m), 2, MidpointRounding.AwayFromZero);
            return new ExpectedEffectResult(
                ExpectedEffectType.AdditionalRevenue,
                additionalRevenue,
                $"Потенциальный дополнительный оборот около {FormatMoney(additionalRevenue)} при увеличении расхода на 20% и сохранении текущего ДРР.");
        }

        if (status == KeywordRecommendationStatus.Watch || insight.Type == InsightType.WatchCandidate)
        {
            return new ExpectedEffectResult(
                ExpectedEffectType.RiskReduction,
                null,
                "Денежный эффект не рассчитывается: рекомендация направлена на снижение риска ошибочного решения.");
        }

        if (status == KeywordRecommendationStatus.LowData || insight.Type == InsightType.LowData)
        {
            return NotCalculated("Ожидаемый эффект не рассчитывается: данных недостаточно.");
        }

        return NotCalculated("Ожидаемый эффект не рассчитывается для этого типа insight.");
    }

    private static ExpectedEffectResult NotCalculated(string text)
    {
        return new ExpectedEffectResult(ExpectedEffectType.NotCalculated, null, text);
    }

    private static RecommendationAction? GetRecommendedAction(
        RecommendationInsight insight,
        KeywordRecommendationStatus status)
    {
        var preferredActions = status switch
        {
            KeywordRecommendationStatus.ToRemove => new[]
            {
                RecommendationAction.ConsiderMinusKeyword,
                RecommendationAction.MinusKeyword,
                RecommendationAction.DecreaseBid,
                RecommendationAction.MoveToWatchlist
            },
            KeywordRecommendationStatus.NeedsAttention => new[]
            {
                RecommendationAction.DecreaseBid,
                RecommendationAction.Optimize,
                RecommendationAction.Watch
            },
            KeywordRecommendationStatus.Effective => new[]
            {
                RecommendationAction.Scale,
                RecommendationAction.IncreaseBidGradually,
                RecommendationAction.FindSimilarKeywords,
                RecommendationAction.Maintain
            },
            KeywordRecommendationStatus.Watch => new[]
            {
                RecommendationAction.Watch,
                RecommendationAction.DecreaseBidCarefully,
                RecommendationAction.CollectMoreData
            },
            KeywordRecommendationStatus.LowData => new[]
            {
                RecommendationAction.CollectMoreData,
                RecommendationAction.Watch
            },
            _ => Array.Empty<RecommendationAction>()
        };

        return preferredActions.FirstOrDefault(action => insight.AllowedActions.Contains(action));
    }

    private static Guid? TryGetEntityId(RecommendationInsight insight)
    {
        var parts = insight.Id.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3
            && string.Equals(parts[0], "keyword", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(parts[1], out var keywordId))
        {
            return keywordId;
        }

        return null;
    }

    private static decimal? GetMetric(RecommendationInsight insight, string name)
    {
        return insight.Metrics.TryGetValue(name, out var value) ? value : null;
    }

    private static string FormatMoney(decimal value)
    {
        return $"{value.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"))} ₽";
    }

    private sealed record ExpectedEffectResult(
        ExpectedEffectType Type,
        decimal? Money,
        string Text);
}
