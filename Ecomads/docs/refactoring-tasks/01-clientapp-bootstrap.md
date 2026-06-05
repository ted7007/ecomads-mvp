# Task 01: bootstrap isolated React frontend

## Цель

Создать изолированный React + TypeScript + Vite frontend в `Ecomads.WebApplication/ClientApp`, не меняя текущий legacy frontend в `Ecomads.WebApplication/wwwroot`.

Эта задача создаёт основу для дальнейшей миграции и должна быть максимально независимой от параллельной разработки legacy-фич.

## Почему это первая задача

- Не затрагивает текущие HTML/JS страницы.
- Даёт безопасную площадку для React-кода.
- Позволяет дальше переносить страницы по одной.
- Минимизирует merge-conflicts с веткой, где дорабатываются текущие фичи.

## Scope

Включить:

- `ClientApp` с Vite + React + TypeScript.
- MUI и базовую theme-конфигурацию.
- React Router.
- TanStack Query provider.
- Базовый `httpClient`.
- Auth token helpers под текущие ключи `localStorage`.
- Первый технический route `/app` или `/app/report-placeholder`.
- Build output в отдельную папку, например `wwwroot/app`.

Не включать:

- Перенос реальных страниц.
- Изменение legacy navigation.
- Удаление или редактирование `wwwroot/*.html`.
- Redux, Next.js, Tailwind.
- Массовый перенос CSS.

## Предлагаемые файлы

```text
Ecomads.WebApplication/
  ClientApp/
    package.json
    vite.config.ts
    tsconfig.json
    index.html
    src/
      main.tsx
      app/
        App.tsx
        router.tsx
        providers.tsx
        theme.ts
      shared/
        api/
          httpClient.ts
          queryClient.ts
        auth/
          tokenStorage.ts
          authTypes.ts
        ui/
          LoadingState.tsx
          ErrorState.tsx
          EmptyState.tsx
```

## Implementation plan

1. Создать `ClientApp`.
2. Добавить зависимости React-стека.
3. Настроить Vite build output в `../wwwroot/app`.
4. Настроить dev proxy для `/api`.
5. Добавить MUI theme с `Inter` и цветами, близкими к legacy CSS.
6. Добавить `QueryClientProvider`.
7. Добавить `BrowserRouter` с временной страницей.
8. Добавить `httpClient`:
   - JSON requests;
   - JWT из `localStorage.ecomads_token`;
   - `401` cleanup;
   - отдельная обработка `FormData`.
9. Добавить `tokenStorage`:
   - `getToken`;
   - `setToken`;
   - `clearAuth`;
   - `getCurrentUser`;
   - `setCurrentUser`.
10. Проверить, что legacy страницы продолжают открываться.

## Backend integration options

Предпочтительный безопасный вариант на первом этапе:

- Не менять `Program.cs`.
- Vite build складывает файлы в `wwwroot/app`.
- React entry доступен как static asset после build.

Если нужно SPA fallback для `/app/*`, делать отдельной задачей после проверки bootstrap. Это уменьшает риск сломать `UseDefaultFiles()` и текущие legacy routes.

## Acceptance criteria

- `ClientApp` собирается локально.
- Legacy files в `wwwroot` не изменены.
- `localStorage` keys совпадают с legacy:
  - `ecomads_token`;
  - `ecomads_user`.
- В коде нет Redux, Next.js, Tailwind.
- Есть единый `httpClient`, а не дубли `fetchWithAuth`.
- Есть отдельная обработка `FormData` без ручного `Content-Type`.

## Manual checks

- Открыть `/index.html`.
- Открыть `/dashboard.html`.
- Открыть `/report.html`.
- Запустить Vite dev server и проверить React placeholder.
- Проверить, что запрос к `/api/auth/me` через proxy уходит на backend.

## Parallel development risk check

Перед merge/rebase проверить:

- Не появились ли изменения в `wwwroot/js/auth.js`, которые требуют синхронизации token storage.
- Не поменялись ли auth response поля в `AuthController`.
- Не поменялся ли expected build/deploy flow.

## Prompt for Codex

```text
Выполни Task 01 из docs/refactoring-tasks/01-clientapp-bootstrap.md.
Создай изолированный ClientApp для React + TypeScript + Vite + MUI.
Legacy-файлы в Ecomads.WebApplication/wwwroot не изменяй.
Не переноси реальные страницы.
Добавь базовый httpClient, tokenStorage, providers, router и MUI theme.
Не добавляй Redux, Next.js, Tailwind.
После изменений перечисли изменённые файлы и команды проверки.
```

