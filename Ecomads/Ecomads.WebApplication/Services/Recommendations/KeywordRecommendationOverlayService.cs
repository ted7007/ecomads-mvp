using System.Text.Json;
using System.Text.Json.Serialization;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Models.Recommendations;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services.Recommendations;

public interface IKeywordRecommendationOverlayService
{
    Task<KeywordRecommendationOverlayDto?> GetOverlayAsync(
        Guid campaignId,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default);
}

public sealed class KeywordRecommendationOverlayService : IKeywordRecommendationOverlayService
{
    private readonly EcomadsDbContext _dbContext;
    private readonly ILogger<KeywordRecommendationOverlayService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public KeywordRecommendationOverlayService(
        EcomadsDbContext dbContext,
        ILogger<KeywordRecommendationOverlayService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<KeywordRecommendationOverlayDto?> GetOverlayAsync(
        Guid campaignId,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default)
    {
        var campaignExists = await _dbContext.Compaigns
            .AnyAsync(campaign => campaign.Id == campaignId, cancellationToken);

        if (!campaignExists)
        {
            return null;
        }

        var keywordStats = await LoadKeywordStatsAsync(campaignId, startDate, endDate, cancellationToken);
        var campaignStats = await LoadCampaignStatsAsync(campaignId, startDate, endDate, cancellationToken);
        var recommendation = await LoadLatestRecommendationAsync(campaignId, cancellationToken);
        var additionalData = DeserializeAdditionalData(recommendation);
        var insights = additionalData?.Insights ?? [];
        var decisions = additionalData?.InsightDecisions ?? new Dictionary<string, InsightDecisionRecord>();

        var keywordInsights = insights
            .Select(insight => new
            {
                Insight = insight,
                KeywordId = TryGetKeywordId(insight)
            })
            .Where(item => item.KeywordId.HasValue)
            .GroupBy(item => item.KeywordId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Insight).ToList());

        var insightDetails = BuildInsightDetails(keywordStats, keywordInsights, decisions);
        var rows = keywordStats
            .Select(keyword => BuildKeywordRow(keyword, keywordInsights.GetValueOrDefault(keyword.Id), decisions))
            .OrderByDescending(row => row.HasInsight)
            .ThenByDescending(row => row.PriorityScore)
            .ThenByDescending(row => row.Spend ?? 0m)
            .ThenBy(row => row.Phrase, StringComparer.Ordinal)
            .ToList();

        var summaryText = BuildSummaryText(keywordStats.Count, rows, recommendation, additionalData);

        return new KeywordRecommendationOverlayDto
        {
            CampaignId = campaignId,
            GeneratedAt = recommendation?.CreatedAt,
            Summary = BuildSummary(campaignStats, keywordStats),
            RecommendationSummary = new RecommendationOverlaySummaryDto
            {
                Text = summaryText,
                GeneratedWithoutLlm = additionalData?.GeneratedWithoutLlm ?? false,
                Counts = BuildCounts(rows)
            },
            Keywords = rows,
            Insights = insightDetails,
            CampaignInsights = BuildCampaignInsights(insights, decisions)
        };
    }

    private async Task<List<KeywordStatistics>> LoadKeywordStatsAsync(
        Guid campaignId,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.KeywordStatistics
            .Where(keyword => keyword.CompaignId == campaignId);

        if (startDate.HasValue)
        {
            var startDateUtc = DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc);
            query = query.Where(keyword => keyword.StartDate >= startDateUtc);
        }

        if (endDate.HasValue)
        {
            var endDateUtc = DateTime.SpecifyKind(endDate.Value, DateTimeKind.Utc);
            query = query.Where(keyword => keyword.EndDate <= endDateUtc);
        }

        return await query.ToListAsync(cancellationToken);
    }

    private async Task<CompaignStatistics?> LoadCampaignStatsAsync(
        Guid campaignId,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.CompaignStatistics
            .Where(stat => stat.CompaignId == campaignId && stat.Type == CompaignStatisticsType.General);

        if (startDate.HasValue)
        {
            var startDateUtc = DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc);
            query = query.Where(stat => stat.StartDate >= startDateUtc);
        }

        if (endDate.HasValue)
        {
            var endDateUtc = DateTime.SpecifyKind(endDate.Value, DateTimeKind.Utc);
            query = query.Where(stat => stat.EndDate <= endDateUtc);
        }

        return await query
            .OrderByDescending(stat => stat.EndDate)
            .ThenByDescending(stat => stat.StartDate)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Recommendation?> LoadLatestRecommendationAsync(
        Guid campaignId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Recommendations
            .Where(recommendation => recommendation.CampaignId == campaignId)
            .OrderByDescending(recommendation => recommendation.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private RecommendationAdditionalData? DeserializeAdditionalData(Recommendation? recommendation)
    {
        if (recommendation == null || string.IsNullOrWhiteSpace(recommendation.AdditionalData))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RecommendationAdditionalData>(
                recommendation.AdditionalData,
                JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Не удалось прочитать AdditionalData рекомендации {RecommendationId}",
                recommendation.Id);
            return null;
        }
    }

    private static KeywordRecommendationRowDto BuildKeywordRow(
        KeywordStatistics keyword,
        IReadOnlyCollection<RecommendationInsight>? insights,
        IReadOnlyDictionary<string, InsightDecisionRecord> decisions)
    {
        var mainInsight = GetMainInsight(insights);
        var status = mainInsight != null
            ? MapStatus(mainInsight.Type)
            : KeywordRecommendationStatus.Neutral;
        var recommendedAction = mainInsight != null
            ? GetRecommendedAction(mainInsight, status)
            : null;
        var decision = mainInsight != null
            ? GetDecision(decisions, mainInsight.Id)
            : null;

        return new KeywordRecommendationRowDto
        {
            KeywordId = keyword.Id,
            Phrase = keyword.Phrase,
            Views = keyword.Impressions,
            Clicks = keyword.Clicks,
            Ctr = ToDecimal(keyword.Ctr),
            Spend = keyword.Spend,
            Orders = keyword.Orders,
            Revenue = keyword.Revenue,
            Drr = GetDisplayDrr(keyword),
            Status = status,
            PriorityScore = mainInsight?.PriorityScore ?? 0,
            PriorityLevel = mainInsight?.PriorityLevel ?? PriorityLevel.Low,
            ConfidenceLevel = mainInsight?.ConfidenceLevel ?? ConfidenceLevel.Low,
            ShortRecommendation = mainInsight != null
                ? GetShortRecommendation(recommendedAction, status)
                : null,
            RecommendedAction = recommendedAction,
            MainInsightId = mainInsight?.Id,
            HasInsight = mainInsight != null,
            DecisionStatus = decision?.DecisionStatus ?? InsightDecisionStatus.None
        };
    }

    private static List<KeywordRecommendationInsightDetailDto> BuildInsightDetails(
        IReadOnlyCollection<KeywordStatistics> keywordStats,
        IReadOnlyDictionary<Guid, List<RecommendationInsight>> keywordInsights,
        IReadOnlyDictionary<string, InsightDecisionRecord> decisions)
    {
        var keywordById = keywordStats.ToDictionary(keyword => keyword.Id);
        var details = new List<KeywordRecommendationInsightDetailDto>();

        foreach (var item in keywordInsights)
        {
            if (!keywordById.TryGetValue(item.Key, out var keyword))
            {
                continue;
            }

            foreach (var insight in item.Value)
            {
                var status = MapStatus(insight.Type);
                var recommendedAction = GetRecommendedAction(insight, status);
                var decision = GetDecision(decisions, insight.Id);

                details.Add(new KeywordRecommendationInsightDetailDto
                {
                    InsightId = insight.Id,
                    KeywordId = keyword.Id,
                    Phrase = keyword.Phrase,
                    Status = status,
                    PriorityScore = insight.PriorityScore,
                    PriorityLevel = insight.PriorityLevel,
                    ConfidenceLevel = insight.ConfidenceLevel,
                    Metrics = new Dictionary<string, decimal?>(insight.Metrics),
                    ReasonCodes = insight.ReasonCodes.ToList(),
                    ShortExplanation = GetShortExplanation(insight),
                    ExpectedEffectText = GetExpectedEffectText(insight),
                    RecommendedActionTitle = GetRecommendedActionTitle(recommendedAction, status),
                    RecommendedActionDescription = GetRecommendedActionDescription(recommendedAction, status),
                    AllowedActions = insight.AllowedActions.ToList(),
                    ForbiddenActions = insight.ForbiddenActions.ToList(),
                    DecisionStatus = decision?.DecisionStatus ?? InsightDecisionStatus.None,
                    UserComment = decision?.UserComment,
                    History = ToHistoryDtos(decision)
                });
            }
        }

        return details
            .OrderByDescending(detail => detail.PriorityScore)
            .ThenByDescending(detail => detail.PriorityLevel)
            .ThenBy(detail => detail.Status)
            .ThenBy(detail => detail.InsightId, StringComparer.Ordinal)
            .ToList();
    }

    private static RecommendationInsight? GetMainInsight(IReadOnlyCollection<RecommendationInsight>? insights)
    {
        return insights?
            .OrderByDescending(insight => insight.PriorityScore)
            .ThenByDescending(insight => insight.PriorityLevel)
            .ThenBy(insight => insight.Type)
            .ThenBy(insight => insight.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static Guid? TryGetKeywordId(RecommendationInsight insight)
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

    private static KeywordRecommendationSummaryDto BuildSummary(
        CompaignStatistics? campaignStats,
        IReadOnlyCollection<KeywordStatistics> keywordStats)
    {
        var orders = keywordStats.Sum(keyword => keyword.Orders ?? 0);

        if (campaignStats != null)
        {
            var revenue = Convert.ToDecimal(campaignStats.Revenue);
            var spend = Convert.ToDecimal(campaignStats.Spend);

            return new KeywordRecommendationSummaryDto
            {
                Earned = revenue,
                Spend = spend,
                Orders = orders,
                Drr = revenue > 0m ? spend / revenue * 100m : null,
                Ctr = ToDecimal(campaignStats.Ctr)
            };
        }

        var keywordRevenue = keywordStats.Sum(keyword => keyword.Revenue ?? 0m);
        var keywordSpend = keywordStats.Sum(keyword => keyword.Spend ?? 0m);
        var impressions = keywordStats.Sum(keyword => keyword.Impressions ?? 0);
        var clicks = keywordStats.Sum(keyword => keyword.Clicks ?? 0);

        return new KeywordRecommendationSummaryDto
        {
            Earned = keywordRevenue,
            Spend = keywordSpend,
            Orders = orders,
            Drr = keywordRevenue > 0m ? keywordSpend / keywordRevenue * 100m : null,
            Ctr = impressions > 0 ? (decimal)clicks / impressions * 100m : null
        };
    }

    private static RecommendationStatusCountsDto BuildCounts(IReadOnlyCollection<KeywordRecommendationRowDto> rows)
    {
        return new RecommendationStatusCountsDto
        {
            ToRemove = rows.Count(row => row.Status == KeywordRecommendationStatus.ToRemove),
            NeedsAttention = rows.Count(row => row.Status == KeywordRecommendationStatus.NeedsAttention),
            Effective = rows.Count(row => row.Status == KeywordRecommendationStatus.Effective),
            Watch = rows.Count(row => row.Status == KeywordRecommendationStatus.Watch),
            LowData = rows.Count(row => row.Status == KeywordRecommendationStatus.LowData),
            Neutral = rows.Count(row => row.Status == KeywordRecommendationStatus.Neutral)
        };
    }

    private static List<CampaignRecommendationInsightDto> BuildCampaignInsights(
        IReadOnlyCollection<RecommendationInsight> insights,
        IReadOnlyDictionary<string, InsightDecisionRecord> decisions)
    {
        return insights
            .Where(insight => insight.EntityType != InsightEntityType.Keyword)
            .OrderByDescending(insight => insight.PriorityScore)
            .Select(insight =>
            {
                var decision = GetDecision(decisions, insight.Id);
                return new CampaignRecommendationInsightDto
                {
                    InsightId = insight.Id,
                    Type = insight.Type,
                    PriorityScore = insight.PriorityScore,
                    PriorityLevel = insight.PriorityLevel,
                    Text = insight.TechnicalComment ?? string.Empty,
                    DecisionStatus = decision?.DecisionStatus ?? InsightDecisionStatus.None,
                    UserComment = decision?.UserComment,
                    History = ToHistoryDtos(decision)
                };
            })
            .ToList();
    }

    private static InsightDecisionRecord? GetDecision(
        IReadOnlyDictionary<string, InsightDecisionRecord> decisions,
        string insightId)
    {
        return decisions.TryGetValue(insightId, out var decision)
            ? decision
            : null;
    }

    private static List<InsightHistoryItemDto> ToHistoryDtos(InsightDecisionRecord? decision)
    {
        return decision?.History
            .Select(item => new InsightHistoryItemDto
            {
                Type = item.Type,
                CreatedAt = item.CreatedAt,
                Comment = item.Comment
            })
            .ToList() ?? [];
    }

    private static string BuildSummaryText(
        int keywordCount,
        IReadOnlyCollection<KeywordRecommendationRowDto> rows,
        Recommendation? recommendation,
        RecommendationAdditionalData? additionalData)
    {
        if (keywordCount == 0)
        {
            return "Загрузите статистику, чтобы система рассчитала рекомендации по ключевым словам.";
        }

        if (recommendation == null || additionalData?.Insights.Count == 0)
        {
            return "По текущим правилам не найдено явных проблем или точек роста.";
        }

        var counts = BuildCounts(rows);
        return $"Найдено: к удалению - {counts.ToRemove}, требуют внимания - {counts.NeedsAttention}, эффективные - {counts.Effective}, наблюдать - {counts.Watch}, мало данных - {counts.LowData}.";
    }

    private static KeywordRecommendationStatus MapStatus(InsightType insightType)
    {
        return insightType switch
        {
            InsightType.BadSpendWithoutOrders => KeywordRecommendationStatus.ToRemove,
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

    private static RecommendationAction? GetRecommendedAction(
        RecommendationInsight insight,
        KeywordRecommendationStatus status)
    {
        var preferredActions = status switch
        {
            KeywordRecommendationStatus.ToRemove => new[]
            {
                RecommendationAction.ConsiderMinusKeyword,
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
            _ => []
        };

        return preferredActions.FirstOrDefault(action => insight.AllowedActions.Contains(action));
    }

    private static string? GetShortRecommendation(
        RecommendationAction? action,
        KeywordRecommendationStatus status)
    {
        if (action.HasValue)
        {
            return action.Value switch
            {
                RecommendationAction.ConsiderMinusKeyword => "Исключить",
                RecommendationAction.DecreaseBid => "Снизить ставку",
                RecommendationAction.DecreaseBidCarefully => "Снизить осторожно",
                RecommendationAction.IncreaseBidGradually => "Повысить ставку",
                RecommendationAction.Scale => "Масштабировать",
                RecommendationAction.CollectMoreData => "Собрать данные",
                RecommendationAction.Watch => "Наблюдать",
                _ => null
            };
        }

        return status switch
        {
            KeywordRecommendationStatus.ToRemove => "Исключить",
            KeywordRecommendationStatus.NeedsAttention => "Снизить ставку",
            KeywordRecommendationStatus.Effective => "Масштабировать",
            KeywordRecommendationStatus.Watch => "Наблюдать",
            KeywordRecommendationStatus.LowData => "Тестировать дальше",
            _ => null
        };
    }

    private static string GetShortExplanation(RecommendationInsight insight)
    {
        if (!string.IsNullOrWhiteSpace(insight.TechnicalComment))
        {
            return insight.TechnicalComment;
        }

        return insight.Type switch
        {
            InsightType.BadSpendWithoutOrders => "Запрос потратил значимый бюджет, но не принес заказов.",
            InsightType.BadDrr => "Запрос приносит заказы, но экономика хуже целевого ДРР.",
            InsightType.ScaleCandidate => "Запрос приносит заказы с приемлемым ДРР и может быть точкой роста.",
            InsightType.WatchCandidate => "Запрос неоднозначный, его лучше оставить под наблюдением.",
            InsightType.LowData => "По запросу пока недостаточно данных для уверенного вывода.",
            _ => "Backend сформировал структурированный insight по этому запросу."
        };
    }

    private static string GetExpectedEffectText(RecommendationInsight insight)
    {
        return insight.Type switch
        {
            InsightType.BadSpendWithoutOrders => "Потенциальная экономия - сокращение неэффективных расходов.",
            InsightType.BadDrr => "Потенциальный эффект - снижение перерасхода после корректировки ставки или карточки.",
            InsightType.ScaleCandidate => "Потенциальный эффект - рост заказов при сохранении ДРР около целевого.",
            InsightType.LowData => "Ожидаемый эффект пока не оценивается, так как данных недостаточно.",
            _ => "Потенциальный эффект зависит от выбранного действия и динамики метрик."
        };
    }

    private static string GetRecommendedActionTitle(
        RecommendationAction? action,
        KeywordRecommendationStatus status)
    {
        return GetShortRecommendation(action, status) switch
        {
            "Исключить" => "Исключить ключевое слово",
            "Снизить ставку" => "Снизить ставку",
            "Снизить осторожно" => "Снизить ставку осторожно",
            "Повысить ставку" => "Повысить ставку постепенно",
            "Масштабировать" => "Масштабировать запрос",
            "Собрать данные" => "Собрать больше данных",
            "Наблюдать" => "Оставить под наблюдением",
            _ => "Проверить запрос"
        };
    }

    private static string GetRecommendedActionDescription(
        RecommendationAction? action,
        KeywordRecommendationStatus status)
    {
        return status switch
        {
            KeywordRecommendationStatus.ToRemove => "Исключите ключевое слово или снизьте ставку, чтобы сократить неэффективный расход.",
            KeywordRecommendationStatus.NeedsAttention => "Скорректируйте ставку или проверьте карточку, чтобы улучшить экономику запроса.",
            KeywordRecommendationStatus.Effective => "Ключ приносит заказы с приемлемой экономикой. Рассмотрите аккуратное масштабирование.",
            KeywordRecommendationStatus.Watch => "Не отключайте запрос резко. Наблюдайте динамику и корректируйте осторожно.",
            KeywordRecommendationStatus.LowData => "Продолжайте тестирование, пока статистики недостаточно для жесткого решения.",
            _ => "По этому запросу нет активного действия."
        };
    }

    private static decimal? GetDisplayDrr(KeywordStatistics keyword)
    {
        if ((keyword.Revenue ?? 0m) <= 0m)
        {
            return null;
        }

        if ((keyword.Spend ?? 0m) == 0m)
        {
            return 0m;
        }

        return keyword.Spend / keyword.Revenue * 100m;
    }

    private static decimal? ToDecimal(double? value)
    {
        return value.HasValue ? Convert.ToDecimal(value.Value) : null;
    }

    private static decimal? ToDecimal(float value)
    {
        return Convert.ToDecimal(value);
    }
}
