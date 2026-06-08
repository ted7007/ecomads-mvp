# Task 03a: recommendation engine foundation

## Цель

Подготовить фундамент рекомендательного движка без изменения поведения текущей генерации рекомендаций.

После этой задачи в проекте должны появиться:

- options для порогов и весов;
- enum и DTO/domain-модели recommendation engine;
- mapper пользовательской цели;
- сервис расчета метрик;
- базовые тесты или явно зафиксированный reason, почему тестовый проект не добавлен.

`RecommendationService` в этой задаче не переписывать.

## Depends on

Прочитать перед началом:

```text
docs/architecture/recommendation-engine.md
docs/guidelines/recommendation-business-rules.md
docs/refactoring-tasks/03-recommendation-engine-mvp.md
Ecomads.WebApplication/Data/Models/KeywordStatistics.cs
Ecomads.WebApplication/Data/Models/CompaignStatistics.cs
Ecomads.WebApplication/Program.cs
```

## Scope

Включить:

- `RecommendationEngineOptions`.
- Registration options in `Program.cs`.
- `RecommendationGoalMapper`.
- Enums:
  - `RecommendationGoal`;
  - `InsightType`;
  - `InsightEntityType`;
  - `PriorityLevel`;
  - `ConfidenceLevel`;
  - `RecommendationAction`.
- Models:
  - `RecommendationInsight`;
  - `CalculatedKeywordMetrics`;
  - `CalculatedCampaignMetrics`;
  - `RecommendationGenerationContext`;
  - `RecommendationAdditionalData`.
- `MetricCalculationService` with safe formulas.
- Unit tests for metric calculation if test project is available or can be added cleanly.

Не включать:

- `InsightGenerationService`.
- `PriorityScoringService`.
- `RecommendationPolicyService`.
- `InsightSelectionService`.
- `LlmRecommendationTextService`.
- Refactor of `RecommendationService`.
- Changes to controllers.
- Changes to upload flow.
- Database schema changes.

## Files to add

```text
Ecomads.WebApplication/Services/Recommendations/RecommendationEngineOptions.cs
Ecomads.WebApplication/Services/Recommendations/RecommendationGoalMapper.cs
Ecomads.WebApplication/Services/Recommendations/MetricCalculationService.cs

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

If tests are added:

```text
Ecomads.WebApplication.Tests/Recommendations/MetricCalculationServiceTests.cs
```

## Files to update

```text
Ecomads.WebApplication/Program.cs
Ecomads.WebApplication/appsettings.json
Ecomads.WebApplication/appsettings.Development.json
```

Only update `.csproj` or `.sln` if a test project is added.

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

## Metric formulas

Implement safe calculations:

```text
DRR = Spend / Revenue * 100
CTR = Clicks / Impressions * 100
CPC = Spend / Clicks
CR = Orders / Clicks * 100
CPO = Spend / Orders
AverageOrderValue = Revenue / Orders
AvgDailyOrders = Orders / PeriodDays
```

Null handling:

- If `Revenue = 0`, `Drr = null`.
- If `Impressions = 0`, `Ctr = null`.
- If `Clicks = 0`, `Cpc = null` and `Cr = null`.
- If `Orders = 0`, `Cpo = null` and `AverageOrderValue = null`.
- If `PeriodDays <= 0`, use `1`.

## Goal mapping

Map current free-form goals:

```text
"снижение ДРР", "оптимизация ДРР", "reduce drr" -> ReduceDrr
"увеличение заказов", "рост заказов" -> IncreaseOrders
"распродажа остатков", "sell out stock" -> SellOutStock
"рост выручки", "рост прибыли", "increase revenue" -> IncreaseRevenue
"удержание позиций", "позиции" -> MaintainPosition
unknown -> IncreaseRevenue
```

## Tests

Metric tests:

- Revenue zero returns `Drr = null`.
- Clicks zero returns `Cpc = null` and `Cr = null`.
- Impressions zero returns `Ctr = null`.
- Orders zero returns `Cpo = null` and `AverageOrderValue = null`.
- PeriodDays zero uses one day.
- Imported spreadsheet `Drr` is not trusted when source fields allow recalculation.

Goal mapper tests:

- Russian legacy goals map to expected enum values.
- Unknown or empty goal maps to `IncreaseRevenue`.

## Acceptance criteria

- Project builds.
- Current `RecommendationService` behavior is unchanged.
- `RecommendationEngineOptions` is registered.
- Metric calculation service is pure and does not depend on EF, HTTP, or LLM.
- New models are serializable without custom converters.
- Tests are added, or final answer explains why no test project was added.

## Stop condition

Stop after this task. Do not implement insight generation, scoring, policy guardrails, or LLM integration in the same pass.
