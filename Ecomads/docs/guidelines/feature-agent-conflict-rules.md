# Feature agent conflict rules

Эти правила предназначены для агента, который параллельно пилит новые legacy-фичи, пока другой агент постепенно мигрирует frontend на React.

## Основное правило

Делать минимальные точечные изменения под фичу и не трогать миграционную область без явной причины.

## Полный промпт

```text
Ты работаешь параллельно с веткой миграции frontend на React.

Чтобы минимизировать конфликты:

1. Не трогай Ecomads.WebApplication/ClientApp и docs, если задача не про миграцию.
2. Если дорабатываешь legacy frontend, меняй только конкретную страницу/JS-модуль, нужный для фичи.
3. Не делай массовый рефакторинг wwwroot/css/style.css, wwwroot/js/api.js, wwwroot/js/auth.js, sidebar.js.
4. Не меняй routes, localStorage keys, auth flow и API response shape без явного согласования.
5. Новые backend endpoints добавляй рядом со старыми, не ломая существующие.
6. Если меняешь API-контракт, сразу зафиксируй:
   - endpoint;
   - request/response shape;
   - affected frontend files;
   - migration impact.
7. Перед изменением report.html, dashboard.html, campaign.html проверь, не переносится ли эта страница в React.
8. Избегай “улучшений заодно”: formatting, renaming, cleanup, перенос CSS — только если это прямо нужно для фичи.
9. В конце задачи напиши список изменённых legacy-файлов и потенциальное влияние на React-миграцию.
```

## Короткая версия для каждого промпта

```text
Работай точечно: не трогай ClientApp/docs, не делай массовый рефакторинг legacy, не меняй API/localStorage/routes без согласования. В конце перечисли изменённые legacy-файлы и возможное влияние на React-миграцию.
```

## Когда использовать полную версию

- Изменения в auth.
- Изменения в API-контрактах.
- Изменения в `wwwroot/js/api.js`.
- Изменения в `wwwroot/js/auth.js`.
- Изменения в `wwwroot/css/style.css`.
- Изменения в `report.html`, `dashboard.html`, `campaign.html`.
- Перед merge/rebase с веткой миграции.

## Что агент должен явно писать в конце задачи

```text
Changed legacy files:
- ...

Potential migration impact:
- ...

API/localStorage/routes changed:
- yes/no
- details if yes
```

