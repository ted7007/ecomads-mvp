# Keyword recommendation overlay architecture

Этот документ фиксирует целевую архитектуру интерфейса рекомендаций как аналитического слоя поверх таблицы ключевых слов.

## Product goal

Рекомендации должны восприниматься не как отдельный текстовый отчет, а как подсказки внутри основной таблицы статистики кампании:

```text
таблица ключевых слов -> подсвеченные строки -> клик по строке -> правая панель insight details
```

Пользователь должен видеть:

- какие ключевые слова требуют действия;
- почему строка подсвечена;
- какие метрики привели к выводу;
- что можно сделать;
- какие действия запрещены guardrails;
- принял, отложил или отклонил ли он конкретный insight.

## Current project state

Текущий рабочий экран кампании находится в legacy frontend:

```text
Ecomads.WebApplication/wwwroot/campaign.html
Ecomads.WebApplication/wwwroot/js/main.js
Ecomads.WebApplication/wwwroot/js/recommendations.js
```

Текущий keyword endpoint:

```text
GET /api/statistics/keywords/{campaignId}
```

Текущий recommendation endpoint:

```text
GET /api/recommendations/campaign/{campaignId}
POST /api/recommendations/generate
PUT /api/recommendations/{id}/status
```

React `CampaignPage` в `ClientApp` пока не реализован. Поэтому MVP UI overlay нужно делать поверх legacy campaign page, а React-перенос планировать отдельным последующим шагом.

## Target API surface

Добавить read endpoint для overlay:

```text
GET /api/recommendations/campaign/{campaignId}/keyword-overlay
```

Query:

```text
startDate=YYYY-MM-DD
endDate=YYYY-MM-DD
```

Response should include:

- campaign summary;
- compact recommendation summary;
- keyword rows with status and short recommendation;
- insight details for side panel;
- campaign-level banners if present.

Add decision endpoints:

```text
POST /api/recommendations/insights/{insightId}/accept
POST /api/recommendations/insights/{insightId}/postpone
POST /api/recommendations/insights/{insightId}/reject
PUT  /api/recommendations/insights/{insightId}/comment
```

These endpoints can store decisions in `Recommendation.AdditionalData` for MVP. No database schema change is required unless the implementation proves JSON updates too brittle.

## Data source strategy

MVP should reuse the latest generated `Recommendation` for the campaign:

1. Load latest recommendation by campaign and period where possible.
2. Deserialize `Recommendation.AdditionalData` into `RecommendationAdditionalData`.
3. Read `insights` and `selectedInsights`.
4. Load keyword statistics for the selected campaign/period.
5. Join keyword rows to insights by keyword identity.

Current generated insight ids use:

```text
keyword:{KeywordStatisticId}:{InsightType}
```

For MVP, `KeywordStatisticId` can be parsed from the insight id. If ids change later, add explicit `EntityId` to `RecommendationInsight`.

## Keyword row status

UI status enum:

```text
ToRemove
NeedsAttention
Effective
Watch
LowData
Neutral
```

Mapping:

```text
BadSpendWithoutOrders   -> ToRemove
BadDrr                  -> NeedsAttention
ScaleCandidate          -> Effective
GoodKeyword             -> Effective
WatchCandidate          -> Watch
LowData                 -> LowData
IrrelevantButConverting -> Watch
PositionGrowthCandidate -> Effective
default                 -> Neutral
```

Main insight rule:

```text
1. Find insights connected to keyword.
2. Sort by priorityScore desc.
3. Pick first as mainInsight.
4. Derive status, shortRecommendation, row highlight from mainInsight.
```

If no insight exists:

```text
status = Neutral
hasInsight = false
shortRecommendation = null
```

## Short recommendation mapping

```text
ToRemove       -> Исключить
NeedsAttention -> Снизить ставку
Effective      -> Масштабировать
Watch          -> Наблюдать
LowData        -> Тестировать дальше
Neutral        -> null
```

Prefer allowed actions when available:

```text
ConsiderMinusKeyword       -> Исключить
DecreaseBid                -> Снизить ставку
DecreaseBidCarefully       -> Снизить осторожно
IncreaseBidGradually       -> Повысить ставку
Scale                      -> Масштабировать
CollectMoreData            -> Собрать данные
Watch                      -> Наблюдать
```

Never show a short recommendation derived from `forbiddenActions`.

## Overlay response shape

```json
{
  "campaignId": "guid",
  "generatedAt": "2026-04-24T06:07:00Z",
  "summary": {
    "earned": 213111,
    "spend": 72384,
    "orders": 3189,
    "drr": 25.4,
    "ctr": 1.78
  },
  "recommendationSummary": {
    "text": "В кампании найдено несколько ключей с расходом без заказов.",
    "generatedWithoutLlm": false,
    "counts": {
      "toRemove": 18,
      "needsAttention": 27,
      "effective": 32,
      "watch": 10,
      "lowData": 14,
      "neutral": 0
    }
  },
  "keywords": [],
  "insights": [],
  "campaignInsights": []
}
```

Keyword row:

```json
{
  "keywordId": "guid",
  "phrase": "keyword",
  "views": 1561,
  "clicks": 41,
  "ctr": 2.63,
  "spend": 742,
  "orders": 0,
  "revenue": 0,
  "drr": null,
  "status": "ToRemove",
  "priorityScore": 84,
  "priorityLevel": "High",
  "confidenceLevel": "Medium",
  "shortRecommendation": "Исключить",
  "recommendedAction": "ConsiderMinusKeyword",
  "mainInsightId": "keyword:guid:BadSpendWithoutOrders",
  "hasInsight": true,
  "decisionStatus": "None"
}
```

Insight detail:

```json
{
  "insightId": "keyword:guid:BadSpendWithoutOrders",
  "keywordId": "guid",
  "phrase": "keyword",
  "status": "ToRemove",
  "priorityScore": 84,
  "priorityLevel": "High",
  "confidenceLevel": "Medium",
  "metrics": {},
  "reasonCodes": [],
  "shortExplanation": "Запрос потратил значимый бюджет, но не принес заказов.",
  "expectedEffectText": "Потенциальная экономия - сокращение неэффективных расходов.",
  "recommendedActionTitle": "Исключить ключевое слово",
  "recommendedActionDescription": "Исключите или снизьте ставку, чтобы сократить неэффективный расход.",
  "allowedActions": [],
  "forbiddenActions": [],
  "decisionStatus": "None",
  "userComment": null,
  "history": []
}
```

## UI behavior

Legacy campaign page MVP:

- Add status filters above keyword table.
- Add `Статус` and `Рекомендация` columns.
- Highlight rows by status.
- Sort default order: active insight first, priorityScore desc, spend desc.
- On row click, open right side panel.
- Side panel shows overview details and decision buttons.
- If no insight exists, show neutral metrics and empty recommendation state.

Do not make a separate recommendations page for this MVP.

## Decision persistence

Decision status values:

```text
None
Accepted
Postponed
Rejected
```

MVP JSON storage:

```json
{
  "insightDecisions": {
    "keyword:guid:BadSpendWithoutOrders": {
      "decisionStatus": "Accepted",
      "userComment": "comment",
      "updatedAt": "2026-04-24T06:07:00Z",
      "history": []
    }
  }
}
```

The implementation can extend `RecommendationAdditionalData` or use an API-specific DTO. Keep old recommendation text/status behavior compatible.

## Implementation phases

1. Backend overlay DTO/API.
2. Insight decision endpoints and JSON persistence.
3. Legacy campaign table overlay and side panel.
4. Optional React campaign page migration.

Do not merge phases 1-3 into one large agent task.
