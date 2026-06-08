# Task 03: recommendation engine MVP

## Цель

Вынести бизнес-логику рекомендаций из LLM в backend-алгоритмы и оставить LLM только для человекочитаемого объяснения готовых инсайтов.

## Декомпозиция реализации

Этот документ является umbrella-plan для всего MVP. Реализацию вести последовательными задачами:

1. `03a-recommendation-engine-foundation.md` — options, models, enums, goal mapper, metric calculation.
2. `03b-recommendation-insights-and-scoring.md` — insight generation, policies, guardrails, scoring, top-N selection.
3. `03c-recommendation-service-llm-integration.md` — подключение к `RecommendationService`, insight-based LLM prompt, persistence в `AdditionalData`, fallback.

Не выполнять весь Task 03 одним заходом. Каждый шаг должен завершаться build/test проверкой и отдельным итогом.

## Current code

Основной файл:

```text
Ecomads.WebApplication/Services/RecommendationService.cs
```

Текущий API-facing контракт:

```csharp
public interface IRecommendationService
{
    Task<Recommendation?> GenerateRecommendationAsync(Guid campaignId, string goal);
}
```

Текущие callers:

```text
Ecomads.WebApplication/Controllers/RecommendationsController.cs
Ecomads.WebApplication/Services/StatisticsBackgroundService.cs
```

Текущая модель сохранения:

```text
Ecomads.WebApplication/Data/Models/Recommendation.cs
```

Для MVP сохранить этот контракт и таблицу. Структурированные инсайты сохранить в `Recommendation.AdditionalData`.

## Scope

Включить:

- Mapping free-form `goal` string в `RecommendationGoal`.
- Options-класс для порогов и весов.
- Расчет keyword-level метрик: DRR, CTR, CPC, CR, CPO, AverageOrderValue, AvgDailyOrders.
- ConfidenceLevel и ConfidenceScore.
- Insight types: `BadSpendWithoutOrders`, `BadDrr`, `ScaleCandidate`, `WatchCandidate`, `LowData`.
- Заготовку для `StockRisk` и `SeasonRisk`, но генерировать их только при наличии входного контекста.
- PriorityScore и PriorityLevel.
- allowedActions и forbiddenActions.
- Top-N selection.
- Новый LLM prompt поверх selected insights.
- Technical fallback при недоступной LLM.
- Базовые unit-тесты.

Не включать:

- Изменение upload flow.
- Изменение legacy frontend.
- Автоматическое управление ставками через API.
- Автоматическое определение семантической нерелевантности.
- Новые таблицы для insights.
- Обязательное изменение публичного request body `/api/recommendations/generate`.

## Files to change

Update:

```text
Ecomads.WebApplication/Program.cs
Ecomads.WebApplication/Services/RecommendationService.cs
Ecomads.WebApplication/appsettings.json
Ecomads.WebApplication/appsettings.Development.json
```

Possibly update only if needed:

```text
Ecomads.WebApplication/Controllers/RecommendationsController.cs
Ecomads.WebApplication/Data/Models/Recommendation.cs
```

Controller should remain compatible with:

```json
{
  "campaignId": "guid",
  "goal": "рост прибыли"
}
```

## New files and classes

Add:

```text
Ecomads.WebApplication/Services/Recommendations/RecommendationEngineOptions.cs
Ecomads.WebApplication/Services/Recommendations/RecommendationGoalMapper.cs
Ecomads.WebApplication/Services/Recommendations/MetricCalculationService.cs
Ecomads.WebApplication/Services/Recommendations/InsightGenerationService.cs
Ecomads.WebApplication/Services/Recommendations/PriorityScoringService.cs
Ecomads.WebApplication/Services/Recommendations/RecommendationPolicyService.cs
Ecomads.WebApplication/Services/Recommendations/InsightSelectionService.cs
Ecomads.WebApplication/Services/Recommendations/RecommendationPromptBuilder.cs
Ecomads.WebApplication/Services/Recommendations/LlmRecommendationTextService.cs
```

Add models:

```text
Ecomads.WebApplication/Models/Recommendations/RecommendationGoal.cs
Ecomads.WebApplication/Models/Recommendations/InsightType.cs
Ecomads.WebApplication/Models/Recommendations/InsightEntityType.cs
Ecomads.WebApplication/Models/Recommendations/PriorityLevel.cs
Ecomads.WebApplication/Models/Recommendations/ConfidenceLevel.cs
Ecomads.WebApplication/Models/Recommendations/RecommendationAction.cs
Ecomads.WebApplication/Models/Recommendations/RecommendationInsight.cs
Ecomads.WebApplication/Models/Recommendations/CalculatedKeywordMetrics.cs
Ecomads.WebApplication/Models/Recommendations/CalculatedCampaignMetrics.cs
Ecomads.WebApplication/Models/Recommendations/RecommendationGenerationContext.cs
Ecomads.WebApplication/Models/Recommendations/RecommendationAdditionalData.cs
```

If there is no test project, add in a separate step or create:

```text
Ecomads.WebApplication.Tests/Ecomads.WebApplication.Tests.csproj
```

Suggested test files:

```text
Ecomads.WebApplication.Tests/Recommendations/MetricCalculationServiceTests.cs
Ecomads.WebApplication.Tests/Recommendations/InsightGenerationServiceTests.cs
Ecomads.WebApplication.Tests/Recommendations/PriorityScoringServiceTests.cs
Ecomads.WebApplication.Tests/Recommendations/RecommendationPolicyServiceTests.cs
Ecomads.WebApplication.Tests/Recommendations/InsightSelectionServiceTests.cs
```

## RecommendationService changes

`RecommendationService` should become orchestration only.

New flow:

1. Create scope and load campaign.
2. Load latest or relevant `CompaignStatistics`.
3. Load all `KeywordStatistics` for the campaign or selected period.
4. Map `goal` string to `RecommendationGoal`.
5. Build `RecommendationGenerationContext`.
6. Calculate metrics.
7. Generate raw insights.
8. Apply policies to fill actions.
9. Score insights.
10. Sort and select top-N.
11. Build prompt from selected insights only.
12. Call `LlmRecommendationTextService`.
13. If LLM fails, build technical fallback text from top insights.
14. Save `Recommendation` with:
    - original `Goal`;
    - prompt;
    - full LLM response or fallback response;
    - parsed text fields where possible;
    - structured `AdditionalData`.

Keep `GenerateRecommendationAsync(Guid campaignId, string goal)` unchanged for callers.

## Business rules in first iteration

Implement:

- `LowData`
- `BadSpendWithoutOrders`
- `BadDrr`
- `ScaleCandidate`
- `WatchCandidate`
- Confidence calculation
- Goal weights
- Impact score
- Urgency score
- Priority score
- Priority level
- Guardrails:
  - do not minus converting keyword;
  - do not scale bad economy;
  - do not make hard conclusions with LowData.

Prepare enums and option hooks for:

- `StockRisk`
- `SeasonRisk`
- `AggressivelyReduceAllSpend`
- `AcceptHigherDrrTemporarily`

Do not generate stock or season insights until the service receives stock and season context.

## Options

Add:

```json
{
  "RecommendationEngine": {
    "TargetDrr": 30,
    "MinClicksForConclusion": 30,
    "MinSpendForConclusion": 500,
    "MinOrdersForPositiveConclusion": 3,
    "MinViewsForCtrConclusion": 1000,
    "MaxInsightsForLlm": 20,
    "PriorityMultiplier": 25
  }
}
```

Register:

```csharp
builder.Services.Configure<RecommendationEngineOptions>(
    builder.Configuration.GetSection("RecommendationEngine"));
```

## LLM prompt

The prompt must contain:

- Goal.
- Target DRR.
- Campaign summary.
- Selected structured insights as JSON.
- Allowed and forbidden actions for every insight.
- Explicit instruction that LLM must not recalculate metrics or invent numbers.

Response format:

```text
1. Краткий вывод
2. Что сделать в первую очередь
3. Что масштабировать
4. Что оставить под наблюдением
5. Риски
```

## Risks

- Current campaign-level statistics do not include orders and impressions, so campaign-level CR and CTR recalculation are limited.
- Current API does not provide target DRR, stock, or season deadline.
- Existing `StatisticsBackgroundService` generates several recommendations after upload, so the new engine must avoid expensive repeated LLM calls.
- `RecommendationService` currently returns `null` on LLM failure. MVP should decide whether to save technical fallback or return it without LLM.
- Existing frontend may show only `RecommendationText`, so fallback text must be useful.
- Existing `AdditionalData` is JSONB, but malformed manual strings would break deserialization.

## Test cases

Metrics:

- Revenue zero returns `Drr = null`.
- Clicks zero returns `Cpc = null` and `Cr = null`.
- Impressions zero returns `Ctr = null`.
- Orders zero returns `Cpo = null` and `AverageOrderValue = null`.
- PeriodDays zero uses one day.

Confidence:

- Below all thresholds gives Low and 0.4.
- At medium threshold gives Medium and 0.7.
- At high threshold gives High and 1.0.

Insights:

- Significant spend, enough clicks, zero orders creates `BadSpendWithoutOrders`.
- Low spend and low clicks with zero orders creates `LowData`, not `BadSpendWithoutOrders`.
- Orders with DRR above target and medium confidence creates `BadDrr`.
- Orders with DRR below target and medium confidence creates `ScaleCandidate`.
- Low confidence creates `WatchCandidate` or `LowData` with aggressive actions forbidden.

Policies:

- Converting keyword with acceptable DRR forbids `ImmediateMinusKeyword`.
- Strong bad DRR forbids `Scale` and `AggressiveScale`.
- Low confidence forbids `ImmediateMinusKeyword`, `AggressiveBidChange`, `Scale`, `AggressiveScale`.

Scoring:

- Same input gives same score and level.
- Bad spend over 3000 with High confidence is at least High.
- Top-N selection returns no more than `MaxInsightsForLlm`.
- Equal-score insights are sorted deterministically by type and entity name.

LLM prompt:

- Prompt does not include raw full keyword table.
- Prompt includes selected insights JSON.
- Prompt includes `forbiddenActions`.
- Prompt includes instruction not to recalculate metrics.

## Acceptance criteria

- Existing recommendation endpoints compile and keep the same request/response shape.
- Existing background generation compiles.
- LLM receives prepared insights only.
- Backend calculates metrics, priorities, and actions before LLM.
- Saved `Recommendation.AdditionalData` contains structured insights.
- Technical fallback exists when LLM call fails.
- Unit tests cover metric calculation, insight generation, policy guardrails, scoring, and selection.
