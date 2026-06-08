# Recommendation engine architecture

Этот документ фиксирует целевую backend-архитектуру рекомендаций. Главный принцип: LLM не является источником истины и не принимает бизнес-решения.

## Current state

Текущая точка входа:

```text
Ecomads.WebApplication/Services/RecommendationService.cs
IRecommendationService.GenerateRecommendationAsync(Guid campaignId, string goal)
```

Сервис вызывается из:

```text
Ecomads.WebApplication/Controllers/RecommendationsController.cs
Ecomads.WebApplication/Services/StatisticsBackgroundService.cs
```

Сейчас `RecommendationService`:

1. Загружает кампанию, общую статистику и ключевые запросы.
2. Выбирает top keywords и worst keywords простыми LINQ-запросами.
3. Передает агрегированные данные в LLM.
4. Просит LLM определить главную проблему и рекомендацию.
5. Парсит текстовый ответ и сохраняет `Recommendation`.

Проблема текущей схемы: LLM сама интерпретирует метрики, классифицирует запросы и выбирает действие.

## Target pipeline

Целевой pipeline:

```text
campaignId + goal
  -> load campaign statistics and keyword statistics
  -> calculate metrics
  -> generate structured insights
  -> apply recommendation policy
  -> score and sort insights
  -> select top-N insights
  -> generate LLM text from prepared insights
  -> save recommendation
```

Источник истины:

```text
uploaded data -> metrics -> business rules -> scoring -> structured insights
```

LLM слой:

```text
structured insights -> readable explanation -> final recommendation text
```

## Responsibilities

### Backend

Backend calculates:

- DRR, CTR, CPC, CR, CPO.
- Average order value where orders and revenue are available.
- Average daily orders.
- Confidence level and confidence score.
- Insight type.
- Priority score and priority level.
- Allowed actions and forbidden actions.
- Top-N insight selection before LLM.

Backend decides:

- Which keyword is a bad spend case.
- Which keyword is a scale candidate.
- Which keyword requires observation.
- Which actions are blocked by guardrails.

### LLM

LLM only:

- Explains backend insights in readable language.
- Groups insights into business-friendly blocks.
- Adapts wording to the user goal.
- Mentions risks supplied by backend.

LLM must not:

- Recalculate metrics.
- Invent new numbers.
- Classify raw keywords.
- Propose actions from `forbiddenActions`.
- Override backend priority.

## Proposed service layout

Add a focused recommendation domain under:

```text
Ecomads.WebApplication/Services/Recommendations/
```

Suggested files:

```text
Services/Recommendations/
  RecommendationEngineOptions.cs
  RecommendationGoalMapper.cs
  MetricCalculationService.cs
  InsightGenerationService.cs
  PriorityScoringService.cs
  RecommendationPolicyService.cs
  InsightSelectionService.cs
  LlmRecommendationTextService.cs
  RecommendationPromptBuilder.cs
```

Suggested interfaces:

```text
IRecommendationMetricCalculationService
IInsightGenerationService
IPriorityScoringService
IRecommendationPolicyService
IInsightSelectionService
ILlmRecommendationTextService
```

Keep `IRecommendationService` as the API-facing orchestration facade to preserve current callers.

## Domain models

Add domain models under:

```text
Ecomads.WebApplication/Models/Recommendations/
```

Suggested files:

```text
RecommendationGoal.cs
InsightType.cs
InsightEntityType.cs
PriorityLevel.cs
ConfidenceLevel.cs
RecommendationAction.cs
RecommendationInsight.cs
CalculatedKeywordMetrics.cs
CalculatedCampaignMetrics.cs
RecommendationGenerationContext.cs
RecommendationGenerationResult.cs
```

Core `RecommendationInsight` shape:

```csharp
public sealed class RecommendationInsight
{
    public string Id { get; init; } = string.Empty;
    public InsightType Type { get; init; }
    public InsightEntityType EntityType { get; init; }
    public string EntityName { get; init; } = string.Empty;

    public double PriorityScore { get; init; }
    public PriorityLevel PriorityLevel { get; init; }

    public double ImpactScore { get; init; }
    public double UrgencyScore { get; init; }
    public double ConfidenceScore { get; init; }
    public ConfidenceLevel ConfidenceLevel { get; init; }

    public IReadOnlyDictionary<string, decimal?> Metrics { get; init; }
        = new Dictionary<string, decimal?>();

    public IReadOnlyCollection<RecommendationAction> AllowedActions { get; init; }
        = Array.Empty<RecommendationAction>();

    public IReadOnlyCollection<RecommendationAction> ForbiddenActions { get; init; }
        = Array.Empty<RecommendationAction>();

    public IReadOnlyCollection<string> ReasonCodes { get; init; }
        = Array.Empty<string>();

    public string? TechnicalComment { get; init; }
}
```

## Goal mapping

Current API accepts a free-form `goal` string. Preserve this contract and map it internally:

```text
"снижение ДРР", "оптимизация ДРР", "reduce drr" -> ReduceDrr
"увеличение заказов", "рост заказов" -> IncreaseOrders
"распродажа остатков", "sell out stock" -> SellOutStock
"рост выручки", "рост прибыли", "increase revenue" -> IncreaseRevenue
"удержание позиций", "позиции" -> MaintainPosition
```

Unknown goals should default to `IncreaseRevenue` and keep the original string in the saved recommendation.

## Data availability in current code

Available now:

```text
CompaignStatistics:
  Revenue
  Spend
  Clicks
  Ctr
  Drr
  StartDate
  EndDate

KeywordStatistics:
  Phrase
  Frequency
  Cpm
  AvgPosition
  Impressions
  Clicks
  Ctr
  Spend
  Orders
  Revenue
  Drr
  StartDate
  EndDate
```

Missing or partial for the full specification:

- `TargetDrr` is not stored in campaign settings or request.
- Campaign-level `Orders` and `Impressions` are not stored in `CompaignStatistics`.
- `Stock` is not stored.
- `DaysUntilDemandDrop` or seasonal deadline is not stored.
- Semantic irrelevance flag is not stored.

MVP should use keyword-level data for most rules and use options defaults where a target is required.

## Configuration

Add options section:

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

`TargetDrr` is a temporary MVP default. Later it should move to user, store, campaign, or request-level settings.

## Storage strategy

Keep the current `recommendations` table for MVP.

Save structured data into `Recommendation.AdditionalData`:

```json
{
  "goalType": "IncreaseRevenue",
  "targetDrr": 30,
  "insights": [],
  "selectedInsights": [],
  "metricsVersion": "recommendation-engine-mvp-v1",
  "generatedWithoutLlm": false
}
```

This avoids a database migration in the first iteration and preserves existing frontend/API behavior.

Later, if insights need separate lifecycle and UI operations, add normalized tables:

```text
recommendation_insights
recommendation_insight_actions
```

## API compatibility

Do not change:

```text
POST /api/recommendations/generate
GET /api/recommendations/campaign/{campaignId}
GET /api/recommendations/stats
```

`POST /api/recommendations/generate` should continue accepting:

```json
{
  "campaignId": "guid",
  "goal": "рост прибыли"
}
```

Optional future API extension:

```json
{
  "campaignId": "guid",
  "goal": "SellOutStock",
  "targetDrr": 25,
  "stock": 120,
  "daysUntilDemandDrop": 21
}
```

Do not add this extension in MVP unless the UI already supplies these fields.

## LLM prompt contract

LLM receives only prepared insights, not raw keyword tables.

Prompt must explicitly state:

```text
You do not calculate metrics.
You do not invent numbers.
You do not propose forbidden actions.
You explain only backend-provided insights.
```

Expected response blocks:

```text
1. Краткий вывод
2. Что сделать в первую очередь
3. Что масштабировать
4. Что оставить под наблюдением
5. Риски
```

`LlmRecommendationTextService` should return a deterministic technical fallback if LLM is unavailable.

## Deterministic ordering

Insight ordering must be stable for equal inputs:

```text
PriorityScore desc
PriorityLevel desc
InsightType asc
EntityType asc
EntityName asc
Id asc
```

Avoid ordering only by score because ties are expected.

## Implementation phases

### Phase 1: MVP recommendation engine

- Add options and domain models.
- Calculate keyword-level metrics.
- Generate required MVP insight types.
- Apply policies and guardrails.
- Score and select top-N insights.
- Replace old LLM prompt with insight-based prompt.
- Save structured insights to `AdditionalData`.

### Phase 2: Campaign-level and stock context

- Add campaign-level orders and impressions, or aggregate them from keywords where reliable.
- Add optional target DRR in request or campaign settings.
- Add stock and season inputs.
- Enable full `StockRisk` and `SeasonRisk` scoring.

### Phase 3: UI and feedback loop

- Expose structured insights to frontend.
- Track accepted/rejected insights.
- Compare effects after recommendation status changes.
- Add client-specific rules.

## Acceptance criteria

- Same input data produces the same insight order.
- LLM prompt contains selected structured insights only.
- Metrics are calculated before LLM call.
- Priority and guardrails are calculated before LLM call.
- `forbiddenActions` are visible in saved `AdditionalData`.
- Existing recommendation endpoints continue working.
- If LLM fails, the system can still save or return technical insight text.
