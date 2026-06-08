using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Models.Recommendations;
using Ecomads.WebApplication.Services.Recommendations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ecomads.WebApplication.Services;

public interface IRecommendationService
{
    Task<Recommendation?> GenerateRecommendationAsync(Guid campaignId, string goal);
}

public class RecommendationService : IRecommendationService
{
    private readonly EcomadsDbContext _dbContext;
    private readonly IRecommendationGoalMapper _goalMapper;
    private readonly IRecommendationMetricCalculationService _metricCalculationService;
    private readonly IInsightGenerationService _insightGenerationService;
    private readonly IRecommendationPolicyService _policyService;
    private readonly IPriorityScoringService _priorityScoringService;
    private readonly IInsightSelectionService _insightSelectionService;
    private readonly IRecommendationPromptBuilder _promptBuilder;
    private readonly ILlmRecommendationTextService _llmRecommendationTextService;
    private readonly RecommendationEngineOptions _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RecommendationService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public RecommendationService(
        EcomadsDbContext dbContext,
        IRecommendationGoalMapper goalMapper,
        IRecommendationMetricCalculationService metricCalculationService,
        IInsightGenerationService insightGenerationService,
        IRecommendationPolicyService policyService,
        IPriorityScoringService priorityScoringService,
        IInsightSelectionService insightSelectionService,
        IRecommendationPromptBuilder promptBuilder,
        ILlmRecommendationTextService llmRecommendationTextService,
        IOptions<RecommendationEngineOptions> options,
        IConfiguration configuration,
        ILogger<RecommendationService> logger)
    {
        _dbContext = dbContext;
        _goalMapper = goalMapper;
        _metricCalculationService = metricCalculationService;
        _insightGenerationService = insightGenerationService;
        _policyService = policyService;
        _priorityScoringService = priorityScoringService;
        _insightSelectionService = insightSelectionService;
        _promptBuilder = promptBuilder;
        _llmRecommendationTextService = llmRecommendationTextService;
        _options = options.Value;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<Recommendation?> GenerateRecommendationAsync(Guid campaignId, string goal)
    {
        var campaign = await _dbContext.Compaigns.FindAsync(campaignId);
        if (campaign == null)
        {
            return null;
        }

        var statistics = await _dbContext.CompaignStatistics
            .Where(stat => stat.CompaignId == campaignId && stat.Type == CompaignStatisticsType.General)
            .OrderByDescending(stat => stat.EndDate)
            .ThenByDescending(stat => stat.StartDate)
            .FirstOrDefaultAsync();

        var keywordStatistics = await LoadKeywordStatisticsAsync(campaignId, statistics);
        var keywordMetrics = keywordStatistics
            .Select(_metricCalculationService.CalculateKeywordMetrics)
            .ToList();

        var campaignMetrics = statistics != null
            ? _metricCalculationService.CalculateCampaignMetrics(statistics, keywordMetrics)
            : null;

        var goalType = _goalMapper.Map(goal);
        var context = new RecommendationGenerationContext
        {
            CampaignId = campaign.Id,
            CampaignName = campaign.Name,
            OriginalGoal = goal,
            Goal = goalType,
            TargetDrr = _options.TargetDrr,
            CampaignMetrics = campaignMetrics,
            KeywordMetrics = keywordMetrics
        };

        var rawInsights = _insightGenerationService.GenerateInsights(context);
        var policyInsights = _policyService.ApplyPolicies(context, rawInsights);
        var scoredInsights = _priorityScoringService.ScoreInsights(context, policyInsights);
        var selectedInsights = _insightSelectionService.SelectForLlm(scoredInsights);

        var prompt = _promptBuilder.BuildPrompt(context, selectedInsights);
        var llmResult = await _llmRecommendationTextService.GenerateTextAsync(prompt);
        var finalText = llmResult.GeneratedWithoutLlm
            ? BuildTechnicalFallbackText(context, selectedInsights, llmResult.Error)
            : llmResult.Text;

        var additionalData = new RecommendationAdditionalData
        {
            GoalType = goalType,
            TargetDrr = _options.TargetDrr,
            Insights = scoredInsights.ToList(),
            SelectedInsights = selectedInsights.ToList(),
            GeneratedWithoutLlm = llmResult.GeneratedWithoutLlm
        };

        var recommendation = new Recommendation
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            CreatedAt = DateTime.UtcNow,
            Goal = goal,
            Prompt = prompt,
            FullResponse = finalText,
            Problem = ExtractSection(finalText, "1.") ?? GetFirstNonEmptyLine(finalText),
            RecommendationText = finalText,
            ExpectedEffect = ExtractSection(finalText, "5.") ?? string.Empty,
            Status = "новая",
            RequestMetadata = JsonSerializer.Serialize(new
            {
                model = _configuration["OpenAI:Model"],
                temperature = 0.3,
                timestamp = DateTime.UtcNow,
                recommendationEngineVersion = additionalData.MetricsVersion,
                generatedWithoutLlm = llmResult.GeneratedWithoutLlm,
                llmError = llmResult.Error
            }, JsonOptions),
            AdditionalData = JsonSerializer.Serialize(additionalData, JsonOptions),
            UserComment = string.Empty
        };

        _dbContext.Recommendations.Add(recommendation);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Рекомендация сохранена в БД: {RecommendationId} для кампании {CampaignId}. Insights: {InsightsCount}, Selected: {SelectedCount}, WithoutLlm: {WithoutLlm}",
            recommendation.Id,
            campaignId,
            scoredInsights.Count,
            selectedInsights.Count,
            llmResult.GeneratedWithoutLlm);

        return recommendation;
    }

    private async Task<List<KeywordStatistics>> LoadKeywordStatisticsAsync(
        Guid campaignId,
        CompaignStatistics? statistics)
    {
        var query = _dbContext.KeywordStatistics
            .Where(keyword => keyword.CompaignId == campaignId);

        if (statistics != null)
        {
            var periodKeywords = await query
                .Where(keyword => keyword.StartDate == statistics.StartDate && keyword.EndDate == statistics.EndDate)
                .ToListAsync();

            if (periodKeywords.Count > 0)
            {
                return periodKeywords;
            }
        }

        return await query.ToListAsync();
    }

    private static string BuildTechnicalFallbackText(
        RecommendationGenerationContext context,
        IReadOnlyCollection<RecommendationInsight> selectedInsights,
        string? llmError)
    {
        var lines = new List<string>
        {
            "1. Краткий вывод",
            selectedInsights.Count == 0
                ? "Backend не нашел достаточно приоритетных инсайтов для уверенной рекомендации."
                : $"Backend нашел {selectedInsights.Count} приоритетных инсайтов для цели {context.Goal}.",
            string.Empty,
            "2. Что сделать в первую очередь"
        };

        AppendInsightsByType(
            lines,
            selectedInsights.Where(insight => insight.PriorityLevel >= PriorityLevel.High),
            "Нет критичных или высокоприоритетных действий.");

        lines.Add(string.Empty);
        lines.Add("3. Что масштабировать");
        AppendInsightsByType(
            lines,
            selectedInsights.Where(insight => insight.Type == InsightType.ScaleCandidate),
            "Нет подтвержденных кандидатов на масштабирование.");

        lines.Add(string.Empty);
        lines.Add("4. Что оставить под наблюдением");
        AppendInsightsByType(
            lines,
            selectedInsights.Where(insight => insight.Type is InsightType.WatchCandidate or InsightType.LowData),
            "Нет отдельных запросов для наблюдения.");

        lines.Add(string.Empty);
        lines.Add("5. Риски");
        lines.Add("Текст сформирован техническим fallback, потому что LLM недоступна или вернула пустой ответ.");

        if (!string.IsNullOrWhiteSpace(llmError))
        {
            lines.Add($"LLM error: {llmError}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendInsightsByType(
        List<string> lines,
        IEnumerable<RecommendationInsight> insights,
        string emptyText)
    {
        var materialized = insights.ToList();
        if (materialized.Count == 0)
        {
            lines.Add(emptyText);
            return;
        }

        foreach (var insight in materialized)
        {
            var allowedActions = insight.AllowedActions.Count == 0
                ? "нет"
                : string.Join(", ", insight.AllowedActions);
            var forbiddenActions = insight.ForbiddenActions.Count == 0
                ? "нет"
                : string.Join(", ", insight.ForbiddenActions);

            lines.Add(
                $"- {insight.EntityName}: {insight.Type}, приоритет {insight.PriorityLevel} ({insight.PriorityScore}). Разрешено: {allowedActions}. Запрещено: {forbiddenActions}.");
        }
    }

    private static string? ExtractSection(string text, string sectionPrefix)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var line = lines.FirstOrDefault(value => value.StartsWith(sectionPrefix, StringComparison.OrdinalIgnoreCase));

        if (line == null)
        {
            return null;
        }

        var separatorIndex = line.IndexOf(':');
        return separatorIndex >= 0 && separatorIndex < line.Length - 1
            ? line[(separatorIndex + 1)..].Trim()
            : line;
    }

    private static string GetFirstNonEmptyLine(string text)
    {
        return text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;
    }
}
