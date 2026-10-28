# EcomAds docs

- [Лендинг, заявки в пилот и администраторы](landing-and-pilot.md)

- [Задача 3 — план страницы рекламной кампании по макету v10](scenario-plans/campaign-ui-task-3.md)
- [Реализованный первый спринт рекомендаций и доступность в сервисе](recommendations-sprint-1.md)
- [План первого спринта с решениями пользователя](scenario-plans/recommendations-sprint-1-plan.md)

Документы сгруппированы так, чтобы Codex мог быстро брать правильный контекст для миграции фронтенда и параллельной разработки.

## Категории

- `architecture/` — целевая структура frontend и API-контракты.
- `guidelines/` — правила кодогенерации и ограничения для Codex.
- `migration/` — общая стратегия постепенной миграции.
- `refactoring-tasks/` — планы конкретных задач рефакторинга.
- `scenario-plans/` — промпты и планы под конкретные рабочие ситуации.
- `expert-review/` — документы для передачи внешним экспертам на оценку бизнес-логики.

## Рекомендуемый порядок чтения

1. `migration/frontend-migration.md`
2. `guidelines/codex-frontend-rules.md`
3. `guidelines/feature-agent-conflict-rules.md`
4. `architecture/frontend-structure.md`
5. `architecture/frontend-api-contracts.md`
6. `refactoring-tasks/01-clientapp-bootstrap.md`
7. `refactoring-tasks/02-report-page-migration.md`

## Рекомендательная система

- [Денежное ранжирование и отметка автопополнения бюджета — дополнение к ТЗ v1.3](recommendation-ranking-and-budget-auto-replenishment.md)

- [Четыре архитектурные схемы EcomAds для домашнего задания: Context, Container и Component](architecture/ecomads-c4-homework.md)

0. `expert-review/recommendation-logic-for-marketplace-expert.md`
1. `architecture/recommendation-engine.md`
2. `guidelines/recommendation-business-rules.md`
3. `refactoring-tasks/03-recommendation-engine-mvp.md`
4. `refactoring-tasks/03a-recommendation-engine-foundation.md`
5. `refactoring-tasks/03b-recommendation-insights-and-scoring.md`
6. `refactoring-tasks/03c-recommendation-service-llm-integration.md`
7. `scenario-plans/recommendation-engine-implementation-prompt.md`

## Интерфейс рекомендаций в таблице ключевых слов

1. `architecture/keyword-recommendation-overlay.md`
2. `refactoring-tasks/04-keyword-recommendation-overlay-mvp.md`
3. `refactoring-tasks/04a-keyword-overlay-api.md`
4. `refactoring-tasks/04b-insight-decision-api.md`
5. `refactoring-tasks/04c-legacy-campaign-keyword-overlay-ui.md`
6. `scenario-plans/keyword-recommendation-overlay-prompts.md`

## Demo access and product analytics

1. `refactoring-tasks/05-demo-access-and-product-analytics-mvp.md`

## Сбор данных Wildberries

- [R03 — достаточность кликов и зависимости страницы результатов](scenario-plans/r03-click-threshold-and-results.md).
- [Страница «Результаты»: реализация, методика и проверки](scenario-plans/results-page-report.md).

- [Автоматическое обновление, история и подготовка Telegram](wb-automatic-refresh.md)
- [Ручная загрузка и источники данных](wb-data-loading-guide.md)

- [Задача 3 — реализация страницы кампании: отчёт и скриншоты](scenario-plans/campaign-ui-task-3-report.md).

- [Задача 4 — кабинет WB и Telegram: аудит блоков и план реализации](scenario-plans/stores-telegram-ui-task-4.md).

- [Задача 4 — реализация кабинета WB и Telegram: отчёт и скриншоты](scenario-plans/stores-telegram-ui-task-4-report.md).
