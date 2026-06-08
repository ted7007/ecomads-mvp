# Task 04a: keyword recommendation overlay API

## Цель

Добавить backend read API, который возвращает keyword table rows вместе с рассчитанными recommendation statuses и insight details.

## Scope

Включить:

- DTO для overlay response.
- Service для построения overlay из `KeywordStatistics` + latest `Recommendation.AdditionalData`.
- Endpoint:

```text
GET /api/recommendations/campaign/{campaignId}/keyword-overlay
```

- Query `startDate`, `endDate` как optional filters.
- Mapping `InsightType -> KeywordRecommendationStatus`.
- Mapping `RecommendationAction -> shortRecommendation`.
- Compact recommendation summary and counts.

Не включать:

- decision endpoints;
- UI changes;
- database schema changes;
- LLM calls;
- recommendation generation changes.

## Suggested files

Add:

```text
Ecomads.WebApplication/Models/Recommendations/KeywordRecommendationStatus.cs
Ecomads.WebApplication/Models/Recommendations/InsightDecisionStatus.cs
Ecomads.WebApplication/Models/Recommendations/KeywordRecommendationOverlayDto.cs
Ecomads.WebApplication/Services/Recommendations/KeywordRecommendationOverlayService.cs
```

Update:

```text
Ecomads.WebApplication/Controllers/RecommendationsController.cs
Ecomads.WebApplication/Program.cs
```

## Response shape

Use the shape from:

```text
docs/architecture/keyword-recommendation-overlay.md
```

## Mapping rules

```text
BadSpendWithoutOrders       -> ToRemove
BadDrr                      -> NeedsAttention
ScaleCandidate              -> Effective
GoodKeyword                 -> Effective
WatchCandidate              -> Watch
LowData                     -> LowData
IrrelevantButConverting     -> Watch
PositionGrowthCandidate     -> Effective
default                     -> Neutral
```

Main insight:

```text
priorityScore desc
priorityLevel desc
type asc
id asc
```

Default table order:

```text
hasInsight desc
priorityScore desc
spend desc
phrase asc
```

## Empty states

If no keyword stats:

```text
keywords = []
recommendationSummary.text = "Загрузите статистику, чтобы система рассчитала рекомендации по ключевым словам."
```

If stats exist but no recommendation or insights:

```text
status = Neutral
hasInsight = false
recommendationSummary.text = "По текущим правилам не найдено явных проблем или точек роста."
```

## Acceptance criteria

- Endpoint returns keyword rows even if no recommendation exists.
- DRR with zero revenue is returned as `null`, not `0`.
- Each keyword has `status`, `hasInsight`, and `decisionStatus`.
- Each insight detail includes allowed and forbidden actions.
- Build succeeds.

## Stop condition

Stop after read API. Do not implement decision endpoints or UI changes.
