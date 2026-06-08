# Task 04c: legacy campaign keyword overlay UI

## Цель

Обновить legacy campaign page так, чтобы таблица ключевых слов показывала recommendation overlay: статусы, подсветку, короткие рекомендации, фильтры и правую панель деталей.

## Depends on

Tasks 04a and 04b must be complete.

## Scope

Включить:

- load overlay endpoint instead of plain keyword stats where available;
- status filters with counts;
- table columns `Статус` and `Рекомендация`;
- row highlight by status;
- default sorting: active insights first, priorityScore desc, spend desc;
- click row -> side panel;
- side panel overview with metrics/explanation/action/risks;
- accept/postpone/reject/comment actions;
- compact recommendation summary above table;
- fallback to current keyword endpoint if overlay endpoint is unavailable.

Не включать:

- React campaign page;
- separate recommendations page;
- charts in side panel;
- bulk actions;
- WB API changes.

## Suggested files

Update:

```text
Ecomads.WebApplication/wwwroot/campaign.html
Ecomads.WebApplication/wwwroot/js/main.js
Ecomads.WebApplication/wwwroot/js/recommendations.js
Ecomads.WebApplication/wwwroot/css/style.css
```

Add if needed:

```text
Ecomads.WebApplication/wwwroot/js/keywordRecommendationOverlay.js
```

## UI statuses

```text
ToRemove        -> К удалению
NeedsAttention  -> Требует внимания
Effective       -> Эффективное
Watch           -> Наблюдать
LowData         -> Мало данных
Neutral         -> Без рекомендации
```

Colors should be restrained:

```text
ToRemove        -> red/burgundy accent
NeedsAttention  -> warning amber
Effective       -> green/teal
Watch           -> blue/neutral
LowData         -> gray
Neutral         -> no highlight
```

## Side panel

Blocks:

- header with phrase and status;
- tabs: `Обзор`, optional `История`;
- why recommended;
- metrics;
- short explanation;
- expected effect;
- recommended action;
- forbidden actions warning;
- decision buttons;
- comment field.

If keyword has no insight:

```text
По этому ключевому слову нет активной рекомендации.
Показатели не выделяются как проблемные или перспективные по текущим правилам.
```

## Acceptance criteria

- Existing campaign page still loads.
- Keyword table works with uploaded stats.
- Rows show status and short recommendation.
- Filters by status work.
- Side panel opens and updates on row click.
- Decision buttons call backend endpoints for the selected insight.
- Comment is saved and shown after reload.
- Long LLM text is not the central element.

## Stop condition

Stop after legacy UI MVP. Do not start React migration.
