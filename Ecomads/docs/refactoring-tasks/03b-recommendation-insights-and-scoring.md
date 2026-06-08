# Task 03b: recommendation insights, policies, and scoring

## Цель

Реализовать алгоритмическую классификацию keyword insights, guardrails, scoring и top-N selection на базе моделей и метрик из Task 03a.

После этой задачи backend должен уметь получить рассчитанные метрики и вернуть отсортированный список структурированных инсайтов без участия LLM.

`RecommendationService` в этой задаче не переписывать.

## Depends on

Task 03a must be complete.

Прочитать перед началом:

```text
docs/architecture/recommendation-engine.md
docs/guidelines/recommendation-business-rules.md
docs/refactoring-tasks/03a-recommendation-engine-foundation.md
Ecomads.WebApplication/Models/Recommendations/*
Ecomads.WebApplication/Services/Recommendations/MetricCalculationService.cs
```

## Scope

Включить:

- `InsightGenerationService`.
- `RecommendationPolicyService`.
- `PriorityScoringService`.
- `InsightSelectionService`.
- DI registrations for these services.
- Unit tests for business rules, policies, scoring, and selection.

Не включать:

- LLM prompt building.
- LLM HTTP calls.
- Refactor of `RecommendationService`.
- Saving insights to `Recommendation.AdditionalData`.
- Controller changes.
- Upload flow changes.
- Database schema changes.

## Files to add

```text
Ecomads.WebApplication/Services/Recommendations/InsightGenerationService.cs
Ecomads.WebApplication/Services/Recommendations/RecommendationPolicyService.cs
Ecomads.WebApplication/Services/Recommendations/PriorityScoringService.cs
Ecomads.WebApplication/Services/Recommendations/InsightSelectionService.cs
```

If tests are available:

```text
Ecomads.WebApplication.Tests/Recommendations/InsightGenerationServiceTests.cs
Ecomads.WebApplication.Tests/Recommendations/RecommendationPolicyServiceTests.cs
Ecomads.WebApplication.Tests/Recommendations/PriorityScoringServiceTests.cs
Ecomads.WebApplication.Tests/Recommendations/InsightSelectionServiceTests.cs
```

## Files to update

```text
Ecomads.WebApplication/Program.cs
```

Only update models from Task 03a if a missing field blocks implementation.

## Business rules to implement

Implement MVP insight types:

```text
LowData
BadSpendWithoutOrders
BadDrr
ScaleCandidate
WatchCandidate
```

Keep enum values and hooks for:

```text
StockRisk
SeasonRisk
IrrelevantButConverting
PositionGrowthCandidate
GoodKeyword
CampaignEfficiencyProblem
CampaignGrowthOpportunity
```

Do not generate stock or season insights until stock and season context exists.

## Confidence

Implement:

```text
High:
  Clicks >= 100
  or Spend >= 3000
  or Orders >= 10

Medium:
  Clicks >= 30
  or Spend >= 500
  or Orders >= 3

Low:
  everything below Medium
```

Scores:

```text
Low = 0.4
Medium = 0.7
High = 1.0
```

## Policies and guardrails

Backend must fill `AllowedActions` and `ForbiddenActions`.

Implement:

- Do not minus converting keyword.
- Do not scale keyword with very bad DRR.
- Do not make hard conclusions with low data.

Required forbidden actions:

```text
ImmediateMinusKeyword
AggressiveBidChange
Scale
AggressiveScale
ImmediateDisable
```

## Scoring

Implement:

- GoalWeight.
- ImpactScore.
- UrgencyScore.
- ConfidenceScore.
- SeasonScore default `1.0`.
- StockRiskScore default `1.0`.
- PriorityScore.
- PriorityLevel.
- Priority overrides from `docs/guidelines/recommendation-business-rules.md`.

Default formula:

```text
PriorityScore = min(100, round(
  GoalWeight
  * ImpactScore
  * UrgencyScore
  * ConfidenceScore
  * SeasonScore
  * StockRiskScore
  * PriorityMultiplier
))
```

## Top-N selection

Implement:

```text
Top 3 Critical
Top 5 High
Top 5 ScaleCandidate
Top 5 WatchCandidate
Top 5 LowData if needed
MaxInsightsForLlm = 20
```

Stable sorting:

```text
PriorityScore desc
PriorityLevel desc
InsightType asc
EntityType asc
EntityName asc
Id asc
```

## Tests

Insights:

- Significant spend, enough clicks, zero orders creates `BadSpendWithoutOrders`.
- Low spend and low clicks with zero orders creates `LowData`, not `BadSpendWithoutOrders`.
- Orders with DRR above target and medium confidence creates `BadDrr`.
- Orders with DRR below target and medium confidence creates `ScaleCandidate`.
- Low confidence creates `WatchCandidate` or `LowData`.

Policies:

- Converting keyword with acceptable DRR forbids `ImmediateMinusKeyword`.
- Strong bad DRR forbids `Scale` and `AggressiveScale`.
- Low confidence forbids `ImmediateMinusKeyword`, `AggressiveBidChange`, `Scale`, `AggressiveScale`.

Scoring:

- Same input gives same score and level.
- Bad spend over 3000 with High confidence is at least High.
- Goal changes affect priority through goal weights.

Selection:

- Returns no more than `MaxInsightsForLlm`.
- Equal-score insights are sorted deterministically by type and entity name.

## Acceptance criteria

- Project builds.
- Unit tests for pure business services pass, or final answer explains test limitation.
- Business services do not depend on EF, HTTP, or LLM.
- Low-data cases are not classified as aggressive bad keyword cases.
- Policies always preserve existing forbidden actions when scoring or selection runs.

## Stop condition

Stop after this task. Do not wire the new engine into `RecommendationService` and do not change the LLM prompt in the same pass.
