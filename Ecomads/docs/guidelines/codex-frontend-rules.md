# Codex frontend generation rules

Эти правила использовать в промптах к Codex при генерации React-кода для EcomAds.

## Главные ограничения

- Не переписывать весь frontend сразу.
- Не менять legacy files в `Ecomads.WebApplication/wwwroot` без явного запроса.
- Новый React-код создавать в `Ecomads.WebApplication/ClientApp`.
- Не менять backend API endpoints без отдельного согласования.
- Не внедрять Redux, Next.js, Tailwind или сложную архитектуру без отдельного обоснования.
- Сохранять совместимость с параллельной разработкой legacy-фич.

## Предпочтительный стек

- React.
- TypeScript.
- Vite.
- MUI.
- React Router.
- TanStack Query.
- Zod.
- React Hook Form.

## API правила

- JWT брать из `localStorage.ecomads_token`.
- Пользователя брать из `localStorage.ecomads_user`.
- При `401` очищать оба ключа и отправлять пользователя на login.
- Для JSON-запросов выставлять `Content-Type: application/json`.
- Для `FormData` не выставлять `Content-Type`; браузер должен сам добавить boundary.
- Не дублировать `fetchWithAuth`; использовать единый `httpClient`.
- API responses валидировать через Zod на границе API.

## UI правила

- Использовать MUI компоненты вместо ручной HTML-разметки, где это разумно.
- Сохранять визуальную близость к текущему интерфейсу.
- Не переносить весь legacy CSS сразу.
- Общие UI-компоненты класть в `shared/ui`.
- Page-specific компоненты держать внутри папки страницы.

## State management

- Server state: `TanStack Query`.
- Form state: `React Hook Form`.
- Validation: `Zod`.
- Local UI state: `useState` / `useReducer`.
- Auth token storage: маленький helper в `shared/auth/tokenStorage.ts`.
- Не добавлять global store, пока нет конкретной проблемы, которую он решает.

## Порядок работы Codex

Перед генерацией кода:

1. Проверить, какая legacy-страница переносится.
2. Найти связанные JS-модули и API endpoints.
3. Зафиксировать, какие legacy-файлы нельзя трогать.
4. Создать или обновить только минимальный набор React-файлов.
5. Добавить типы и Zod-схемы для новых API-запросов.
6. Проверить, что новая страница работает рядом с legacy.

## Запрещённые действия без явного запроса

- Удалять `wwwroot/*.html`.
- Удалять `wwwroot/js/*.js`.
- Массово менять `wwwroot/css/style.css`.
- Переключать все routes на React сразу.
- Менять auth storage keys.
- Менять значения статусов рекомендаций.
- Делать `git commit`.

## Хороший промпт для генерации

```text
Перенеси только страницу <имя страницы> на React в ClientApp.
Legacy-файлы в wwwroot не изменяй.
Используй React + TypeScript + Vite + MUI + TanStack Query + Zod.
Сохрани текущие API endpoints и localStorage keys.
Добавь минимальные компоненты и типы.
Не добавляй Redux, Next.js, Tailwind.
После изменений перечисли изменённые файлы и риски.
```

