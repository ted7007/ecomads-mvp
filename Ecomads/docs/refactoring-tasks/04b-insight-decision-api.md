# Task 04b: insight decision API

## Цель

Добавить backend endpoints для решения пользователя по конкретному insight: принять, отложить, отклонить, сохранить комментарий.

## Depends on

Task 04a must be complete.

## Scope

Включить endpoints:

```text
POST /api/recommendations/insights/{insightId}/accept
POST /api/recommendations/insights/{insightId}/postpone
POST /api/recommendations/insights/{insightId}/reject
PUT  /api/recommendations/insights/{insightId}/comment
```

Включить:

- update of latest matching `Recommendation.AdditionalData`;
- decision status values: `None`, `Accepted`, `Postponed`, `Rejected`;
- `userComment`;
- minimal `history` events;
- response with updated insight detail or overlay response fragment.

Не включать:

- normalized DB table;
- bulk actions;
- applying changes to WB API;
- frontend changes.

## Suggested files

Update:

```text
Ecomads.WebApplication/Controllers/RecommendationsController.cs
Ecomads.WebApplication/Services/Recommendations/KeywordRecommendationOverlayService.cs
Ecomads.WebApplication/Models/Recommendations/KeywordRecommendationOverlayDto.cs
Ecomads.WebApplication/Models/Recommendations/RecommendationAdditionalData.cs
```

Add if useful:

```text
Ecomads.WebApplication/Services/Recommendations/InsightDecisionService.cs
```

## JSON persistence

MVP can store decisions in `Recommendation.AdditionalData`:

```json
{
  "insightDecisions": {
    "insight-id": {
      "decisionStatus": "Accepted",
      "userComment": "comment",
      "updatedAt": "2026-04-24T06:07:00Z",
      "history": [
        {
          "type": "Accepted",
          "createdAt": "2026-04-24T06:07:00Z",
          "comment": null
        }
      ]
    }
  }
}
```

Preserve existing fields:

```text
goalType
targetDrr
insights
selectedInsights
metricsVersion
generatedWithoutLlm
```

## Acceptance criteria

- Decision endpoints update only the requested insight.
- Unknown insight returns `404`.
- Comment update preserves decision status.
- Overlay read API reflects updated decision status and comment.
- Build succeeds.

## Stop condition

Stop after backend decision API. Do not implement frontend UI.
