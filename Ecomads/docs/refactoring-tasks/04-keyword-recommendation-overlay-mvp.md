# Task 04: keyword recommendation overlay MVP

## Цель

Сделать рекомендации прикладным слоем поверх таблицы ключевых слов кампании: статус строки, короткая рекомендация, подсветка, фильтры и правая панель деталей.

Этот документ является umbrella-plan. Реализацию вести последовательными задачами:

1. `04a-keyword-overlay-api.md` — backend read API для таблицы и деталей.
2. `04b-insight-decision-api.md` — endpoints для принятия, откладывания, отклонения и комментария к insight.
3. `04c-legacy-campaign-keyword-overlay-ui.md` — legacy UI overlay в `campaign.html`/`main.js`.

React `CampaignPage` не включать в MVP, потому что текущая рабочая campaign page находится в legacy frontend.

## Current state

Legacy campaign screen:

```text
Ecomads.WebApplication/wwwroot/campaign.html
Ecomads.WebApplication/wwwroot/js/main.js
Ecomads.WebApplication/wwwroot/js/recommendations.js
```

Existing keyword endpoint:

```text
GET /api/statistics/keywords/{campaignId}
```

Existing recommendation generation:

```text
POST /api/recommendations/generate
GET /api/recommendations/campaign/{campaignId}
```

Recommendation engine already saves structured insights in:

```text
Recommendation.AdditionalData
```

## MVP Scope

Включить:

- keyword row status;
- short recommendation in table;
- row highlighting;
- status filters with counts;
- click row -> right side panel;
- side panel metrics, explanation, expected effect, recommended action;
- accept/postpone/reject/comment for concrete insight;
- compact recommendation summary above table.

Не включать:

- separate recommendations page;
- React campaign page migration;
- WB API bid changes;
- bulk actions;
- normalized insight tables;
- advanced history matching between regenerated insights;
- big charts in side panel.

## Backend data requirements

Frontend needs:

```text
keywordId
phrase
views
clicks
ctr
spend
orders
revenue
drr
status
priorityScore
priorityLevel
confidenceLevel
shortRecommendation
recommendedAction
mainInsightId
hasInsight
decisionStatus
```

Side panel needs:

```text
insightId
keywordId
phrase
status
priorityScore
priorityLevel
confidenceLevel
metrics
reasonCodes
shortExplanation
expectedEffectText
recommendedActionTitle
recommendedActionDescription
allowedActions
forbiddenActions
decisionStatus
userComment
history
```

## Risks

- Current `RecommendationInsight` does not have explicit `EntityId`; MVP must parse keyword id from insight id or add a non-breaking field.
- Existing keyword endpoint does not include recommendation status.
- `Recommendation.AdditionalData` JSON updates must preserve existing insight data.
- Legacy page has direct DOM rendering; keep changes focused.
- LLM may be unavailable, but overlay must still work from backend insights.

## Acceptance criteria

- Campaign page remains the primary screen.
- Keyword table has status and short recommendation columns.
- Rows with insights are highlighted.
- Filters work by row status.
- Right panel opens for selected keyword.
- Accept/postpone/reject/comment work for a concrete insight.
- Long LLM text is not the central UI.
