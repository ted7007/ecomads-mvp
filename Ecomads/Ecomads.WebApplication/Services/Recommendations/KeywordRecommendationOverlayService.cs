using System.Text.Json;
using System.Text.Json.Serialization;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Models.Recommendations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
    private readonly RecommendationEngineOptions _options;
    private readonly ILogger<KeywordRecommendationOverlayService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public KeywordRecommendationOverlayService(
        EcomadsDbContext dbContext,
        IOptions<RecommendationEngineOptions> options,
        ILogger<KeywordRecommendationOverlayService> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
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
        var insights = recommendation == null
            ? new List<OverlayInsight>()
            : (await LoadInsightsAsync(recommendation.Id, cancellationToken))
                .Select(ToOverlayInsight)
                .ToList();

        var keywordInsights = insights
            .Where(insight => insight.EntityType == InsightEntityType.Keyword && insight.EntityId.HasValue)
            .Select(insight => new { Insight = insight, KeywordId = insight.EntityId!.Value })
            .GroupBy(item => item.KeywordId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Insight).ToList());

        var insightDetails = BuildInsightDetails(keywordStats, keywordInsights, _options);
        var rows = keywordStats
            .Select(keyword => BuildKeywordRow(keyword, keywordInsights.GetValueOrDefault(keyword.Id), _options))
            .OrderByDescending(row => row.HasInsight)
            .ThenByDescending(row => row.PriorityScore)
            .ThenByDescending(row => row.Spend ?? 0m)
            .ThenBy(row => row.Phrase, StringComparer.Ordinal)
            .ToList();

        var summaryText = BuildSummaryText(keywordStats.Count, rows, recommendation);

        return new KeywordRecommendationOverlayDto
        {
            CampaignId = campaignId,
            GeneratedAt = recommendation?.CreatedAt,
            Summary = BuildSummary(campaignStats, keywordStats),
            RecommendationSummary = new RecommendationOverlaySummaryDto
            {
                Text = summaryText,
                GeneratedWithoutLlm = IsGeneratedWithoutLlm(recommendation),
                Counts = BuildCounts(rows)
            },
            Keywords = rows,
            Insights = insightDetails,
            CampaignInsights = BuildCampaignInsights(insights)
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

    private async Task<List<RecommendationInsightEntity>> LoadInsightsAsync(
        Guid recommendationRunId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.RecommendationInsights
            .Where(insight => insight.RecommendationRunId == recommendationRunId)
            .ToListAsync(cancellationToken);
    }

    private static OverlayInsight ToOverlayInsight(RecommendationInsightEntity entity)
    {
        return new OverlayInsight
        {
            Id = entity.Id,
            EntityType = entity.EntityType,
            EntityId = entity.EntityId,
            EntityName = entity.EntityName,
            Type = entity.InsightType,
            Status = entity.Status,
            PriorityScore = entity.PriorityScore,
            PriorityLevel = entity.PriorityLevel,
            ConfidenceLevel = entity.ConfidenceLevel,
            RecommendedAction = entity.RecommendedAction,
            ExpectedEffectType = entity.ExpectedEffectType,
            ExpectedEffectMoney = entity.ExpectedEffectMoney,
            ExpectedEffectText = entity.ExpectedEffectText,
            Metrics = DeserializeJson<Dictionary<string, decimal?>>(entity.MetricsJson) ?? new Dictionary<string, decimal?>(),
            ReasonCodes = DeserializeJson<List<string>>(entity.ReasonCodesJson) ?? [],
            AllowedActions = DeserializeJson<List<RecommendationAction>>(entity.AllowedActionsJson) ?? [],
            ForbiddenActions = DeserializeJson<List<RecommendationAction>>(entity.ForbiddenActionsJson) ?? [],
            DecisionStatus = entity.DecisionStatus,
            UserComment = entity.UserComment
        };
    }

    private static T? DeserializeJson<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
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
        IReadOnlyCollection<OverlayInsight>? insights,
        RecommendationEngineOptions options)
    {
        var mainInsight = GetMainInsight(insights);
        var status = mainInsight != null
            ? mainInsight.Status
            : KeywordRecommendationStatus.Neutral;
        var recommendedAction = mainInsight != null
            ? GetRecommendedAction(mainInsight, status) ?? mainInsight.RecommendedAction
            : null;

        return new KeywordRecommendationRowDto
        {
            KeywordId = keyword.Id,
            Phrase = keyword.Phrase,
            Views = keyword.Impressions,
            Clicks = keyword.Clicks,
            Ctr = GetDisplayCtr(keyword),
            Spend = keyword.Spend,
            Orders = keyword.Orders,
            Revenue = keyword.Revenue,
            Drr = GetDisplayDrr(keyword),
            Status = status,
            PriorityScore = mainInsight?.PriorityScore ?? 0,
            PriorityLevel = GetDisplayPriorityLevel(
                mainInsight,
                keyword.Spend ?? 0m,
                keyword.Clicks ?? 0,
                options),
            ConfidenceLevel = mainInsight?.ConfidenceLevel ?? ConfidenceLevel.Low,
            ShortRecommendation = mainInsight != null
                ? GetShortRecommendation(recommendedAction, status)
                : null,
            RecommendedAction = recommendedAction,
            ExpectedEffectType = mainInsight?.ExpectedEffectType ?? ExpectedEffectType.NotCalculated,
            ExpectedEffectMoney = mainInsight?.ExpectedEffectMoney,
            ExpectedEffectText = mainInsight?.ExpectedEffectText ?? string.Empty,
            MainInsightId = mainInsight?.Id,
            HasInsight = mainInsight != null,
            DecisionStatus = mainInsight?.DecisionStatus ?? InsightDecisionStatus.None
        };
    }

    private static List<KeywordRecommendationInsightDetailDto> BuildInsightDetails(
        IReadOnlyCollection<KeywordStatistics> keywordStats,
        IReadOnlyDictionary<Guid, List<OverlayInsight>> keywordInsights,
        RecommendationEngineOptions options)
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
                var status = insight.Status;
                var recommendedAction = GetRecommendedAction(insight, status) ?? insight.RecommendedAction;

                details.Add(new KeywordRecommendationInsightDetailDto
                {
                    InsightId = insight.Id,
                    KeywordId = keyword.Id,
                    Phrase = keyword.Phrase,
                    Status = status,
                    PriorityScore = insight.PriorityScore,
                    PriorityLevel = GetDisplayPriorityLevel(
                        insight,
                        keyword.Spend ?? 0m,
                        keyword.Clicks ?? 0,
                        options),
                    ConfidenceLevel = insight.ConfidenceLevel,
                    Metrics = new Dictionary<string, decimal?>(insight.Metrics),
                    ReasonCodes = insight.ReasonCodes.ToList(),
                    ShortExplanation = GetShortExplanation(insight),
                    ExpectedEffectType = insight.ExpectedEffectType,
                    ExpectedEffectMoney = insight.ExpectedEffectMoney,
                    ExpectedEffectText = insight.ExpectedEffectText,
                    RecommendedActionTitle = GetRecommendedActionTitle(recommendedAction, status),
                    RecommendedActionDescription = GetRecommendedActionDescription(recommendedAction, status),
                    AllowedActions = insight.AllowedActions.ToList(),
                    ForbiddenActions = insight.ForbiddenActions.ToList(),
                    DecisionStatus = insight.DecisionStatus,
                    UserComment = insight.UserComment,
                    History = []
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

    private static OverlayInsight? GetMainInsight(IReadOnlyCollection<OverlayInsight>? insights)
    {
        return insights?
            .OrderByDescending(insight => insight.PriorityScore)
            .ThenByDescending(insight => insight.PriorityLevel)
            .ThenBy(insight => insight.Type)
            .ThenBy(insight => insight.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static PriorityLevel GetDisplayPriorityLevel(
        OverlayInsight? insight,
        decimal spend,
        int clicks,
        RecommendationEngineOptions options)
    {
        if (insight == null)
        {
            return PriorityLevel.Low;
        }

        if (insight.Type != InsightType.BadSpendWithoutOrders
            || clicks < options.MinClicksForConclusion)
        {
            return insight.PriorityLevel;
        }

        if (spend >= options.MinSpendForConclusion * 2m
            && insight.PriorityLevel < PriorityLevel.High)
        {
            return PriorityLevel.High;
        }

        if (spend >= options.MinSpendForConclusion
            && insight.PriorityLevel < PriorityLevel.Medium)
        {
            return PriorityLevel.Medium;
        }

        return insight.PriorityLevel;
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
                Ctr = GetAggregateCtr(keywordStats) ?? NormalizeImportedPercent(campaignStats.Ctr)
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
            Ctr = GetAggregateCtr(keywordStats)
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
        IReadOnlyCollection<OverlayInsight> insights)
    {
        return insights
            .Where(insight => insight.EntityType != InsightEntityType.Keyword)
            .OrderByDescending(insight => insight.PriorityScore)
            .Select(insight => new CampaignRecommendationInsightDto
            {
                InsightId = insight.Id,
                Type = insight.Type,
                PriorityScore = insight.PriorityScore,
                PriorityLevel = insight.PriorityLevel,
                Text = GetShortExplanation(insight),
                ExpectedEffectType = insight.ExpectedEffectType,
                ExpectedEffectMoney = insight.ExpectedEffectMoney,
                ExpectedEffectText = insight.ExpectedEffectText,
                DecisionStatus = insight.DecisionStatus,
                UserComment = insight.UserComment,
                History = []
            })
            .ToList();
    }

    private static string BuildSummaryText(
        int keywordCount,
        IReadOnlyCollection<KeywordRecommendationRowDto> rows,
        Recommendation? recommendation)
    {
        if (keywordCount == 0)
        {
            return "Загрузите статистику, чтобы система рассчитала рекомендации по ключевым словам.";
        }

        if (recommendation == null || rows.All(row => !row.HasInsight))
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
            InsightType.SemanticIrrelevant => KeywordRecommendationStatus.ToRemove,
            InsightType.PositionGrowthCandidate => KeywordRecommendationStatus.Effective,
            _ => KeywordRecommendationStatus.Neutral
        };
    }

    private static RecommendationAction? GetRecommendedAction(
        OverlayInsight insight,
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

    private static string GetShortExplanation(OverlayInsight insight)
    {
        return insight.Type switch
        {
            InsightType.BadSpendWithoutOrders => "Запрос потратил значимый бюджет, но не принес заказов.",
            InsightType.BadDrr => "Запрос приносит заказы, но экономика хуже целевого ДРР.",
            InsightType.ScaleCandidate => "Запрос приносит заказы с приемлемым ДРР и может быть точкой роста.",
            InsightType.WatchCandidate => "Запрос неоднозначный, его лучше оставить под наблюдением.",
            InsightType.LowData => "По запросу пока недостаточно данных для уверенного вывода.",
            InsightType.SemanticIrrelevant => "Запрос не соответствует товару по смыслу и может привлекать нерелевантный трафик.",
            _ => "Backend сформировал структурированный insight по этому запросу."
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

    private static decimal? GetDisplayCtr(KeywordStatistics keyword)
    {
        var clicks = keyword.Clicks ?? 0;
        var impressions = keyword.Impressions ?? 0;

        if (impressions > 0)
        {
            return (decimal)clicks / impressions * 100m;
        }

        return NormalizeImportedPercent(keyword.Ctr);
    }

    private static decimal? GetAggregateCtr(IReadOnlyCollection<KeywordStatistics> keywordStats)
    {
        var impressions = keywordStats.Sum(keyword => keyword.Impressions ?? 0);
        if (impressions <= 0)
        {
            return null;
        }

        var clicks = keywordStats.Sum(keyword => keyword.Clicks ?? 0);
        return (decimal)clicks / impressions * 100m;
    }

    private static decimal? NormalizeImportedPercent(double? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var converted = Convert.ToDecimal(value.Value);
        return converted is > 0m and <= 1m
            ? converted * 100m
            : converted;
    }

    private bool IsGeneratedWithoutLlm(Recommendation? recommendation)
    {
        return DeserializeAdditionalData(recommendation)?.GeneratedWithoutLlm ?? false;
    }

    private static decimal? ToDecimal(double? value)
    {
        return value.HasValue ? Convert.ToDecimal(value.Value) : null;
    }

    private static decimal? ToDecimal(float value)
    {
        return Convert.ToDecimal(value);
    }

    private sealed class OverlayInsight
    {
        public string Id { get; init; } = string.Empty;
        public InsightEntityType EntityType { get; init; }
        public Guid? EntityId { get; init; }
        public string EntityName { get; init; } = string.Empty;
        public InsightType Type { get; init; }
        public KeywordRecommendationStatus Status { get; init; } = KeywordRecommendationStatus.Neutral;
        public double PriorityScore { get; init; }
        public PriorityLevel PriorityLevel { get; init; }
        public ConfidenceLevel ConfidenceLevel { get; init; }
        public RecommendationAction? RecommendedAction { get; init; }
        public ExpectedEffectType ExpectedEffectType { get; init; } = ExpectedEffectType.NotCalculated;
        public decimal? ExpectedEffectMoney { get; init; }
        public string ExpectedEffectText { get; init; } = string.Empty;
        public Dictionary<string, decimal?> Metrics { get; init; } = new();
        public List<string> ReasonCodes { get; init; } = new();
        public List<RecommendationAction> AllowedActions { get; init; } = new();
        public List<RecommendationAction> ForbiddenActions { get; init; } = new();
        public InsightDecisionStatus DecisionStatus { get; init; } = InsightDecisionStatus.None;
        public string? UserComment { get; init; }
    }
}
