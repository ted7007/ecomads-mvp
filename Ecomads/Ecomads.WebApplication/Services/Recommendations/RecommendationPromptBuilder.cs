using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ecomads.WebApplication.Models.Recommendations;
using Microsoft.Extensions.Options;

namespace Ecomads.WebApplication.Services.Recommendations;

public interface IRecommendationPromptBuilder
{
    string BuildPrompt(
        RecommendationGenerationContext context,
        IReadOnlyCollection<RecommendationInsight> selectedInsights);
}

public sealed class RecommendationPromptBuilder : IRecommendationPromptBuilder
{
    private readonly RecommendationEngineOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public RecommendationPromptBuilder(IOptions<RecommendationEngineOptions> options)
    {
        _options = options.Value;
    }

    public string BuildPrompt(
        RecommendationGenerationContext context,
        IReadOnlyCollection<RecommendationInsight> selectedInsights)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(selectedInsights);

        var payload = new
        {
            goal = context.Goal,
            originalGoal = context.OriginalGoal,
            targetDrr = context.TargetDrr,
            campaign = new
            {
                id = context.CampaignId,
                name = context.CampaignName,
                metrics = context.CampaignMetrics
            },
            selectedInsights = selectedInsights.Select(insight => new
            {
                insight.Id,
                insight.Type,
                insight.EntityType,
                insight.EntityName,
                insight.PriorityScore,
                insight.PriorityLevel,
                insight.ImpactScore,
                insight.UrgencyScore,
                insight.ConfidenceScore,
                insight.ConfidenceLevel,
                insight.Metrics,
                insight.AllowedActions,
                insight.ForbiddenActions,
                insight.ReasonCodes,
                insight.TechnicalComment
            })
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);

        return $@"
Ты эксперт по рекламе Wildberries. Backend уже рассчитал метрики, классифицировал инсайты, посчитал приоритеты и определил allowedActions/forbiddenActions.

Жесткие правила:
- Ты не считаешь метрики самостоятельно.
- Ты не придумываешь новые числа.
- Ты не классифицируешь ключевые запросы заново.
- Ты не предлагаешь действия из forbiddenActions.
- Ты объясняешь только те инсайты, которые переданы backend.
- Если confidenceLevel = Low, формулируй вывод осторожно.

Цель пользователя: {context.OriginalGoal}
Внутренний тип цели: {context.Goal}
Целевой ДРР MVP: {context.TargetDrr}
Максимум инсайтов для LLM в конфигурации: {_options.MaxInsightsForLlm}

Структурированные данные от backend:
```json
{json}
```

Сформируй ответ строго в формате:
1. Краткий вывод
2. Что сделать в первую очередь
3. Что масштабировать
4. Что оставить под наблюдением
5. Риски

Пиши конкретно, но не добавляй действий и чисел, которых нет в JSON.
";
    }
}
