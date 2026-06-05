# Task 02: migrate report page to React

## Цель

Перенести `Ecomads.WebApplication/wwwroot/report.html` в новую React-страницу, не удаляя и не ломая legacy `report.html`.

Первый production-кандидат должен жить рядом с legacy-версией и использовать текущий endpoint `/api/recommendations/stats`.

## Почему это вторая задача

- `report.html` — самая изолированная бизнес-страница.
- Она содержит много UI-состояния, которое React упростит.
- Она меньше связана с загрузкой файлов и деталями кампании, чем `dashboard.html` и `campaign.html`.
- Её можно проверить через параллельный route без переключения всех пользователей.

## Scope

Включить:

- `ReportPage`.
- Запрос `/api/recommendations/stats?period=...` через TanStack Query.
- Zod-схему ответа.
- Period select.
- KPI карточки.
- Status summary/progress cards.
- Recommendations table.
- Loading/error/empty states.
- Визуально близкую MUI-версию.

Не включать:

- Удаление `report.html`.
- Переключение legacy sidebar на React route.
- Изменение backend endpoint.
- Генерацию/изменение рекомендаций.
- Большой charting dependency без необходимости.

## Предлагаемые файлы

```text
Ecomads.WebApplication/ClientApp/src/
  pages/
    ReportPage/
      ReportPage.tsx
      reportApi.ts
      reportSchemas.ts
      reportTypes.ts
      components/
        RecommendationStatsCards.tsx
        RecommendationChart.tsx
        RecommendationStatusSummary.tsx
        RecommendationsTable.tsx
  shared/
    lib/
      formatDate.ts
```

Если layout уже создан в Task 01:

```text
Ecomads.WebApplication/ClientApp/src/shared/ui/AppLayout.tsx
Ecomads.WebApplication/ClientApp/src/shared/ui/Sidebar.tsx
```

## API contract

Endpoint:

```text
GET /api/recommendations/stats?period=week|month|quarter|year
```

Expected shape:

```json
{
  "counts": {
    "accepted": 1,
    "pending": 2,
    "rejected": 3
  },
  "monthly": [
    {
      "month": "июн.",
      "accepted": 1,
      "pending": 2,
      "rejected": 3,
      "total": 6
    }
  ],
  "recommendations": [
    {
      "id": "guid",
      "text": "Recommendation text",
      "status": "новая",
      "date": "2026-06-05T00:00:00Z",
      "campaign": "Campaign name",
      "comment": "User comment"
    }
  ]
}
```

## Implementation plan

1. Добавить route `/app/report`.
2. Создать `reportSchemas.ts`:
   - `recommendationCountsSchema`;
   - `monthlyStatsSchema`;
   - `recommendationDetailSchema`;
   - `recommendationStatsResponseSchema`.
3. Создать `reportApi.ts`:
   - `getRecommendationStats(period)`;
   - validation через Zod;
   - нормализация отсутствующих массивов в пустые массивы только в adapter-слое.
4. Создать `ReportPage.tsx`:
   - state выбранного периода;
   - `useQuery`;
   - loading/error states;
   - композиция виджетов.
5. Создать KPI cards для accepted/pending/rejected.
6. Создать status summary с процентами и progress bars.
7. Создать простой chart без новой тяжёлой библиотеки:
   - MUI/Box based bar chart;
   - или CSS flex bars.
8. Создать recommendations table.
9. Проверить визуальную близость к legacy.
10. Оставить legacy `/report.html` без изменений.

## UI details

Периоды:

- `week` — неделя.
- `month` — месяц.
- `quarter` — квартал.
- `year` — год.

Статусы:

- `accepted` или `принято` показывать как `Принято`.
- `pending`, `отложено`, `новая` показывать как `Отложено` или `Новая` по контексту.
- `rejected` или `отклонено` показывать как `Отклонено`.
- Не менять backend status values в этой задаче.

Empty states:

- Если нет monthly data: показать `Нет данных для отображения`.
- Если нет recommendations: показать пустую строку таблицы или MUI empty state.

## Acceptance criteria

- `/app/report` показывает React-версию отчёта.
- `/report.html` продолжает работать.
- Endpoint `/api/recommendations/stats` не изменён.
- Ответ API валидируется через Zod.
- Нет прямых DOM-мутаций.
- Нет inline handlers.
- Нет Redux, Next.js, Tailwind.
- Нет изменений в legacy `report.html`.

## Manual checks

- Открыть legacy `/report.html`.
- Открыть React `/app/report`.
- Проверить periods: week, month, quarter, year.
- Проверить loading state.
- Проверить error state при недоступном API.
- Проверить пустые массивы `monthly` и `recommendations`.
- Проверить auth redirect при `401`.

## Parallel development risk check

Перед началом:

- Проверить, менялась ли на параллельной ветке логика `report.html`.
- Проверить, менялся ли `RecommendationsController.GetRecommendationsStats`.
- Проверить, не поменялись ли названия статусов рекомендаций.

Перед merge:

- Сравнить текущую ветку с параллельной по файлам:
  - `Ecomads.WebApplication/wwwroot/report.html`;
  - `Ecomads.WebApplication/wwwroot/js/api.js`;
  - `Ecomads.WebApplication/Controllers/RecommendationsController.cs`.
- Если backend contract изменился, обновить только `reportSchemas.ts` и adapter, не переписывать страницу.

## Prompt for Codex

```text
Выполни Task 02 из docs/refactoring-tasks/02-report-page-migration.md.
Перенеси только report page в React route /app/report.
Legacy report.html и остальные файлы wwwroot не изменяй.
Используй TanStack Query для /api/recommendations/stats и Zod для валидации ответа.
Сделай MUI-компоненты для KPI, status summary, chart и recommendations table.
Не добавляй Redux, Next.js, Tailwind и тяжёлую chart-библиотеку без обоснования.
После изменений перечисли изменённые файлы, проверки и оставшиеся риски.
```

