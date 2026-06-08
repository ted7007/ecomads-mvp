# Prompts for keyword recommendation overlay MVP

Use these prompts one at a time. Do not ask an agent to implement the full overlay in one pass.

## Step 1: Overlay Read API

```text
Выполни Task 04a из docs/refactoring-tasks/04a-keyword-overlay-api.md.

Цель: добавить backend read API для таблицы ключевых слов с recommendation statuses и insight details.

Контекст обязательно прочитать:
1. docs/architecture/keyword-recommendation-overlay.md
2. docs/refactoring-tasks/04-keyword-recommendation-overlay-mvp.md
3. docs/refactoring-tasks/04a-keyword-overlay-api.md
4. docs/architecture/recommendation-engine.md
5. Ecomads.WebApplication/Services/RecommendationService.cs
6. Ecomads.WebApplication/Models/Recommendations/*
7. Ecomads.WebApplication/Controllers/RecommendationsController.cs
8. Ecomads.WebApplication/Data/Models/KeywordStatistics.cs
9. Ecomads.WebApplication/Data/Models/Recommendation.cs

Перед изменением кода верни короткий plan:
1. Какие DTO будут добавлены.
2. Как latest Recommendation будет связываться с keyword rows.
3. Как будет выбран mainInsight.
4. Как будет строиться status/shortRecommendation.
5. Какие edge cases будут обработаны.

Реализуй только:
- overlay DTO;
- KeywordRecommendationOverlayService;
- GET /api/recommendations/campaign/{campaignId}/keyword-overlay;
- status and short recommendation mapping;
- empty states;
- DI registration.

Не реализуй:
- decision endpoints;
- UI changes;
- DB schema changes;
- LLM calls.

После изменений запусти dotnet build. В финале перечисли измененные файлы, проверки и что осталось для Step 2.

Остановись после Step 1.
```

## Step 2: Insight Decision API

```text
Выполни Task 04b из docs/refactoring-tasks/04b-insight-decision-api.md.

Предусловие: Task 04a уже выполнен.

Цель: добавить endpoints для принятия, откладывания, отклонения и комментария по конкретному insight.

Контекст обязательно прочитать:
1. docs/architecture/keyword-recommendation-overlay.md
2. docs/refactoring-tasks/04b-insight-decision-api.md
3. Ecomads.WebApplication/Controllers/RecommendationsController.cs
4. Ecomads.WebApplication/Services/Recommendations/KeywordRecommendationOverlayService.cs
5. Ecomads.WebApplication/Models/Recommendations/RecommendationAdditionalData.cs

Перед изменением кода верни короткий plan:
1. Где будет храниться decision state.
2. Какие endpoints будут добавлены.
3. Как будет искаться insight по id.
4. Как будет сохраняться history.
5. Как overlay API увидит обновленный status/comment.

Реализуй только:
- decision status DTO/model updates;
- accept/postpone/reject/comment endpoints;
- JSON persistence in Recommendation.AdditionalData;
- minimal history events;
- integration with overlay read response.

Не реализуй:
- frontend UI;
- normalized DB tables;
- bulk actions;
- WB API changes.

После изменений запусти dotnet build. В финале перечисли измененные файлы, проверки и что осталось для Step 3.

Остановись после Step 2.
```

## Step 3: Legacy Campaign UI Overlay

```text
Выполни Task 04c из docs/refactoring-tasks/04c-legacy-campaign-keyword-overlay-ui.md.

Предусловие: Task 04a и Task 04b уже выполнены.

Цель: обновить legacy campaign page, чтобы рекомендации отображались прямо в таблице ключевых слов.

Контекст обязательно прочитать:
1. docs/architecture/keyword-recommendation-overlay.md
2. docs/refactoring-tasks/04c-legacy-campaign-keyword-overlay-ui.md
3. docs/guidelines/codex-frontend-rules.md
4. Ecomads.WebApplication/wwwroot/campaign.html
5. Ecomads.WebApplication/wwwroot/js/main.js
6. Ecomads.WebApplication/wwwroot/js/recommendations.js
7. Ecomads.WebApplication/wwwroot/css/style.css

Перед изменением кода верни короткий plan:
1. Где сейчас рендерится keyword table.
2. Как будет загружаться overlay endpoint.
3. Какие колонки и фильтры будут добавлены.
4. Как будет устроена right side panel.
5. Как будут вызываться decision endpoints.
6. Какой fallback будет при недоступном overlay API.

Реализуй только:
- status filters with counts;
- status and recommendation columns;
- row highlights;
- default overlay sorting;
- side panel with selected keyword insight details;
- accept/postpone/reject/comment buttons;
- compact recommendation summary;
- fallback to old keyword stats endpoint.

Не реализуй:
- React campaign page;
- separate recommendations page;
- charts;
- bulk actions;
- WB API changes.

После изменений запусти доступные frontend/backend проверки. Если dev server нужен, открой страницу через Browser plugin и проверь визуально.

В финале перечисли измененные файлы, проверки и оставшиеся ограничения.

Остановись после Step 3.
```
