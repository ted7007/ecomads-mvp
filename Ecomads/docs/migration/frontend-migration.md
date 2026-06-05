# Frontend migration plan

Цель: постепенно перевести фронтенд EcomAds с HTML/CSS/JS на React + TypeScript + Vite + MUI без остановки разработки текущих фич.

## Короткий вывод

Миграция целесообразна, но только по strangler-подходу: новый React-фронтенд добавляется рядом с текущим `wwwroot`, отдельные страницы переводятся по одной, legacy-страницы остаются рабочими до полной замены.

Не делать полный rewrite. Это создаст высокий риск конфликтов с параллельной разработкой и поломает бизнес-поток.

## Текущий фронтенд

Основные страницы:

- `Ecomads.WebApplication/wwwroot/index.html` — вход и регистрация.
- `Ecomads.WebApplication/wwwroot/dashboard.html` — главный дашборд, KPI, список кампаний, загрузка статистики.
- `Ecomads.WebApplication/wwwroot/campaign.html` — карточка кампании, KPI, ключевые слова, рекомендации.
- `Ecomads.WebApplication/wwwroot/report.html` — отчёт эффективности рекомендаций.

Основные JS-модули:

- `Ecomads.WebApplication/wwwroot/js/auth.js` — auth, token storage, logout.
- `Ecomads.WebApplication/wwwroot/js/api.js` — API-запросы, часть mock/fallback логики.
- `Ecomads.WebApplication/wwwroot/js/main.js` — dashboard и campaign DOM-логика.
- `Ecomads.WebApplication/wwwroot/js/modal.js` — upload modal и загрузка файлов.
- `Ecomads.WebApplication/wwwroot/js/recommendations.js` — рекомендации по кампании.
- `Ecomads.WebApplication/wwwroot/js/sidebar.js` — sidebar и logout.

Главные проблемы текущего фронтенда:

- Логика, разметка и стили смешаны в HTML.
- Используются прямые DOM-мутации и inline handlers.
- Есть дублирование `fetchWithAuth`.
- Типы API не зафиксированы.
- Большие страницы сложны для безопасного изменения, особенно `report.html` и `campaign.html`.

## Приоритет переноса

1. `report.html`
   - Наиболее изолированная страница.
   - Хорошо подходит для React-состояния: period filter, KPI, chart, progress cards, table.
   - Низкий риск затронуть текущие бизнес-фичи.

2. `index.html`
   - Формы входа и регистрации хорошо ложатся на `React Hook Form + Zod`.
   - Важно сохранить ключи `localStorage`: `ecomads_token`, `ecomads_user`.

3. Общие компоненты
   - `AppLayout`
   - `Sidebar`
   - `KpiCard`
   - `DataTable`
   - `UploadModal`
   - `PeriodFilter`

4. `dashboard.html`
   - Средний риск.
   - Есть загрузка статистики и таблица кампаний.
   - Переносить после готового auth/API/layout слоя.

5. `campaign.html`
   - Самый рискованный экран.
   - Завязан на query params, keyword stats, upload modal, recommendations generate/update.
   - Переносить последним или по виджетам.

## Рекомендуемый способ подключения

Новый фронтенд создать рядом с legacy:

```text
Ecomads.WebApplication/
  ClientApp/
  wwwroot/
```

Vite build направлять в отдельную папку внутри static files:

```text
Ecomads.WebApplication/wwwroot/app/
```

Первый React route:

```text
/app/report
```

Legacy route оставить:

```text
/report.html
```

После проверки можно переключить ссылку sidebar с `/report.html` на `/app/report`, но не удалять legacy-страницу сразу.

## Этапы миграции

### Этап 1. Подготовка

- Добавить `ClientApp`.
- Настроить Vite + React + TypeScript.
- Добавить MUI, React Router, TanStack Query, Zod, React Hook Form.
- Настроить dev proxy на backend API.
- Не менять legacy HTML/JS.

### Этап 2. API слой

- Создать единый `httpClient`.
- Поддержать JWT из `localStorage`.
- При `401` очищать `ecomads_token` и `ecomads_user`.
- Для `FormData` не выставлять `Content-Type` вручную.
- Описать DTO и Zod-схемы для используемых endpoints.

### Этап 3. Первый экран

- Перенести `report.html` в `ReportPage`.
- Использовать `useQuery` для `/api/recommendations/stats`.
- Сделать loading/error/empty states.
- Сравнить визуально с legacy-страницей.

### Этап 4. Auth

- Перенести login/register.
- Использовать `React Hook Form + Zod`.
- Сохранить текущий формат token/user storage.
- Проверить redirect-логику.

### Этап 5. Layout и Dashboard

- Перенести sidebar/layout.
- Перенести `dashboard.html`.
- Использовать `useQuery` для campaigns и periods.
- Upload modal сделать отдельной feature.

### Этап 6. Campaign

- Переносить частями:
  - summary KPI;
  - keyword stats table;
  - recommendations list;
  - recommendation status update;
  - recommendation generation;
  - keyword upload.

### Этап 7. Удаление legacy

- Удалять legacy-страницу только после того, как:
  - React-страница используется в production-like сценарии;
  - API-контракты стабилизированы;
  - параллельная ветка не содержит изменений этой legacy-страницы;
  - есть ручная проверка критичных сценариев.

## Что не внедрять без отдельного обоснования

- Redux.
- Next.js.
- Tailwind.
- Сложную feature-sliced/domain architecture.
- Codegen OpenAPI, пока backend API не стабилизирован.

