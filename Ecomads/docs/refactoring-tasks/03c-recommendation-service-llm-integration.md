# Task 03c: recommendation service and LLM integration

## Цель

Подключить готовый recommendation engine к текущему `RecommendationService`, заменить raw-data LLM prompt на insight-based prompt и сохранить совместимость существующего API.

После этой задачи текущий endpoint генерации рекомендаций должен использовать backend-calculated insights, а LLM должна только формулировать текст.

## Depends on

Task 03a and Task 03b must be complete.

Прочитать перед началом:

```text
docs/architecture/recommendation-engine.md
docs/guidelines/recommendation-business-rules.md
docs/refactoring-tasks/03a-recommendation-engine-foundation.md
docs/refactoring-tasks/03b-recommendation-insights-and-scoring.md
Ecomads.WebApplication/Services/RecommendationService.cs
Ecomads.WebApplication/Data/Models/Recommendation.cs
Ecomads.WebApplication/Controllers/RecommendationsController.cs
Ecomads.WebApplication/Services/StatisticsBackgroundService.cs
```

## Scope

Включить:

- `RecommendationPromptBuilder`.
- `LlmRecommendationTextService`.
- Refactor `RecommendationService` into orchestration over new services.
- Selected insights JSON in LLM prompt.
- Technical fallback when LLM is unavailable.
- Saving structured insights in `Recommendation.AdditionalData`.
- Existing API compatibility.
- Integration-oriented tests where practical.

Не включать:

- Upload flow changes.
- Legacy frontend changes.
- Database schema changes if `AdditionalData` is enough.
- New insight tables.
- Auto bid changes through external API.
- New public endpoint unless strictly needed for debugging.

## Files to add

```text
Ecomads.WebApplication/Services/Recommendations/RecommendationPromptBuilder.cs
Ecomads.WebApplication/Services/Recommendations/LlmRecommendationTextService.cs
```

If tests are available:

```text
Ecomads.WebApplication.Tests/Recommendations/RecommendationPromptBuilderTests.cs
Ecomads.WebApplication.Tests/Recommendations/RecommendationServiceTests.cs
```

## Files to update

```text
Ecomads.WebApplication/Services/RecommendationService.cs
Ecomads.WebApplication/Program.cs
```

Possibly update:

```text
Ecomads.WebApplication/Data/Models/Recommendation.cs
Ecomads.WebApplication/Controllers/RecommendationsController.cs
```

Only update controller/model if compatibility or serialization requires it.

## RecommendationService flow

Keep this public contract:

```csharp
Task<Recommendation?> GenerateRecommendationAsync(Guid campaignId, string goal);
```

New internal flow:

1. Load campaign.
2. Load latest or relevant `CompaignStatistics`.
3. Load `KeywordStatistics` for the same campaign and preferably same period.
4. Map free-form `goal` to `RecommendationGoal`.
5. Build `RecommendationGenerationContext`.
6. Calculate metrics.
7. Generate insights.
8. Apply policies.
9. Score insights.
10. Select top-N insights.
11. Build LLM prompt from selected insights only.
12. Call `LlmRecommendationTextService`.
13. If LLM fails, build technical fallback text from selected insights.
14. Save `Recommendation`.

## Prompt rules

LLM prompt must include:

- Goal.
- Target DRR.
- Campaign summary.
- Selected structured insights as JSON.
- `allowedActions`.
- `forbiddenActions`.
- Explicit instruction not to recalculate metrics.
- Explicit instruction not to invent numbers.
- Explicit instruction not to propose forbidden actions.

LLM prompt must not include:

- Full raw keyword table.
- Requests to classify keywords from scratch.
- Requests to choose allowed or forbidden actions.

Expected response blocks:

```text
1. Краткий вывод
2. Что сделать в первую очередь
3. Что масштабировать
4. Что оставить под наблюдением
5. Риски
```

## Persistence

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

If LLM is unavailable:

```json
{
  "generatedWithoutLlm": true
}
```

`RecommendationText` should still be useful for the existing frontend.

## Compatibility

Do not change request shape:

```json
{
  "campaignId": "guid",
  "goal": "рост прибыли"
}
```

Do not change endpoint URLs:

```text
POST /api/recommendations/generate
GET /api/recommendations/campaign/{campaignId}
GET /api/recommendations/stats
```

Existing `StatisticsBackgroundService` must still compile and call the same service method.

## Tests

Prompt:

- Prompt includes selected insights JSON.
- Prompt includes `forbiddenActions`.
- Prompt includes instruction not to recalculate metrics.
- Prompt does not include a raw full keyword table.

Fallback:

- LLM failure creates or returns useful technical text.
- `generatedWithoutLlm` is true in additional data.

Service orchestration:

- Same input orders selected insights deterministically.
- Saved recommendation contains structured `AdditionalData`.
- Existing API-facing method signature is unchanged.

## Acceptance criteria

- Existing recommendation endpoints compile and keep the same request/response shape.
- Existing background generation compiles.
- LLM receives prepared selected insights only.
- Backend calculates metrics, priorities, and actions before LLM.
- Saved `Recommendation.AdditionalData` contains full and selected insights.
- If LLM fails, current frontend still gets readable `RecommendationText`.
- Build and available tests pass.

## Stop condition

Stop after this task. Do not start frontend work, stock/season UI work, bid automation, or database normalization.
