# Prompts for recommendation engine MVP

Этот файл намеренно разбивает реализацию recommendation engine на три последовательных захода. Не давай агенту весь MVP одним prompt-ом: задача слишком широкая, результат будет трудно проверить.

## Как использовать

1. Запусти только Step 1.
2. Проверь diff, build и тесты.
3. После принятия результата запусти Step 2.
4. После принятия результата запусти Step 3.

Каждый prompt ниже содержит stop condition. Агент не должен переходить к следующему шагу в том же заходе, даже если осталось время.

## Step 1: Foundation and Metrics

```text
Выполни Task 03a из docs/refactoring-tasks/03a-recommendation-engine-foundation.md.

Цель этого шага: подготовить фундамент recommendation engine без изменения текущего поведения генерации рекомендаций.

Контекст обязательно прочитать:
1. docs/architecture/recommendation-engine.md
2. docs/guidelines/recommendation-business-rules.md
3. docs/refactoring-tasks/03-recommendation-engine-mvp.md
4. docs/refactoring-tasks/03a-recommendation-engine-foundation.md
5. Ecomads.WebApplication/Data/Models/KeywordStatistics.cs
6. Ecomads.WebApplication/Data/Models/CompaignStatistics.cs
7. Ecomads.WebApplication/Program.cs

Перед изменением кода верни короткий implementation plan:
1. Какие файлы будут изменены.
2. Какие новые модели и enum будут добавлены.
3. Как будет устроен MetricCalculationService.
4. Какие тесты будут добавлены или почему тестовый проект не добавляется.

Реализуй только:
- RecommendationEngineOptions;
- registration options in Program.cs;
- RecommendationGoalMapper;
- enum и модели из Task 03a;
- MetricCalculationService с безопасными формулами;
- тесты на метрики и goal mapping, если тестовый проект есть или его можно добавить cleanly.

Не реализуй:
- InsightGenerationService;
- RecommendationPolicyService;
- PriorityScoringService;
- InsightSelectionService;
- LLM prompt;
- refactor RecommendationService;
- controller changes;
- database schema changes.

После изменений запусти доступные проверки:
- dotnet build;
- dotnet test, если есть тестовый проект.

В финале перечисли:
- измененные файлы;
- добавленные файлы;
- проверки;
- что осталось для Step 2.

Остановись после Step 1. Не переходи к генерации инсайтов и скорингу.
```

## Step 2: Insights, Policies, and Scoring

```text
Выполни Task 03b из docs/refactoring-tasks/03b-recommendation-insights-and-scoring.md.

Предусловие: Step 1 / Task 03a уже выполнен и принят.

Цель этого шага: реализовать алгоритмическую классификацию инсайтов, guardrails, scoring и top-N selection без LLM и без переписывания RecommendationService.

Контекст обязательно прочитать:
1. docs/architecture/recommendation-engine.md
2. docs/guidelines/recommendation-business-rules.md
3. docs/refactoring-tasks/03a-recommendation-engine-foundation.md
4. docs/refactoring-tasks/03b-recommendation-insights-and-scoring.md
5. Ecomads.WebApplication/Models/Recommendations/*
6. Ecomads.WebApplication/Services/Recommendations/MetricCalculationService.cs
7. Ecomads.WebApplication/Program.cs

Перед изменением кода верни короткий implementation plan:
1. Какие сервисы будут добавлены.
2. Какие insight types войдут в этот шаг.
3. Какие guardrails будут реализованы.
4. Как будет считаться PriorityScore.
5. Какие тесты будут добавлены.

Реализуй только:
- InsightGenerationService;
- RecommendationPolicyService;
- PriorityScoringService;
- InsightSelectionService;
- DI registrations;
- тесты на insight rules, policies, scoring и selection.

Business rules MVP:
- LowData;
- BadSpendWithoutOrders;
- BadDrr;
- ScaleCandidate;
- WatchCandidate;
- ConfidenceLevel and ConfidenceScore;
- allowedActions;
- forbiddenActions;
- PriorityScore;
- PriorityLevel;
- deterministic top-N selection.

Не реализуй:
- LLM prompt;
- LLM HTTP calls;
- refactor RecommendationService;
- saving Recommendation.AdditionalData;
- controller changes;
- upload flow changes;
- database schema changes.

После изменений запусти доступные проверки:
- dotnet build;
- dotnet test, если есть тестовый проект.

В финале перечисли:
- измененные файлы;
- добавленные файлы;
- проверки;
- что осталось для Step 3.

Остановись после Step 2. Не подключай engine к RecommendationService в этом же заходе.
```

## Step 3: RecommendationService and LLM Integration

```text
Выполни Task 03c из docs/refactoring-tasks/03c-recommendation-service-llm-integration.md.

Предусловие: Step 1 / Task 03a и Step 2 / Task 03b уже выполнены и приняты.

Цель этого шага: подключить готовый recommendation engine к текущему RecommendationService, заменить raw-data LLM prompt на insight-based prompt и сохранить совместимость API.

Контекст обязательно прочитать:
1. docs/architecture/recommendation-engine.md
2. docs/guidelines/recommendation-business-rules.md
3. docs/refactoring-tasks/03a-recommendation-engine-foundation.md
4. docs/refactoring-tasks/03b-recommendation-insights-and-scoring.md
5. docs/refactoring-tasks/03c-recommendation-service-llm-integration.md
6. Ecomads.WebApplication/Services/RecommendationService.cs
7. Ecomads.WebApplication/Data/Models/Recommendation.cs
8. Ecomads.WebApplication/Controllers/RecommendationsController.cs
9. Ecomads.WebApplication/Services/StatisticsBackgroundService.cs
10. Ecomads.WebApplication/Program.cs

Перед изменением кода верни короткий implementation plan:
1. Как изменится RecommendationService.
2. Какие новые LLM-related классы будут добавлены.
3. Как будет строиться prompt.
4. Что будет сохраняться в Recommendation.AdditionalData.
5. Как будет работать technical fallback.
6. Какие проверки будут запущены.

Реализуй только:
- RecommendationPromptBuilder;
- LlmRecommendationTextService;
- orchestration refactor of RecommendationService;
- LLM prompt from selected structured insights only;
- technical fallback when LLM is unavailable;
- saving structured insights in Recommendation.AdditionalData;
- compatibility with existing POST /api/recommendations/generate;
- tests for prompt/fallback/service orchestration where practical.

Не меняй:
- upload flow;
- legacy frontend;
- existing recommendation endpoint URLs;
- database schema, если AdditionalData достаточно;
- public request shape for /api/recommendations/generate.

LLM не должна:
- считать метрики;
- придумывать новые числа;
- выбирать forbidden/allowed actions;
- получать полную сырую таблицу ключевых слов.

После изменений запусти доступные проверки:
- dotnet build;
- dotnet test, если есть тестовый проект.

В финале перечисли:
- измененные файлы;
- добавленные файлы;
- проверки;
- какие ограничения остались из-за отсутствия target DRR, stock и season context.

Остановись после Step 3. Не начинай frontend, stock/season UI, bid automation или database normalization.
```
