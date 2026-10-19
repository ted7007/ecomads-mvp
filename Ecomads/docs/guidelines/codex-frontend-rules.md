# Codex frontend generation rules

Эти правила использовать в промптах к Codex при генерации React-кода для EcomAds.

## Главные ограничения

- Не переписывать весь frontend сразу.
- Единственный исходник frontend — React в `Ecomads.WebApplication/ClientApp`; `wwwroot` — генерируемая сборка, не второй интерфейс.
- React-код создавать в `Ecomads.WebApplication/ClientApp`.
- Не менять backend API endpoints без отдельного согласования.
- Не внедрять Redux, Next.js, Tailwind или сложную архитектуру без отдельного обоснования.
- Не восстанавливать старые самостоятельные HTML-экраны и legacy CSS/JS; legacy-фронт удалён по запросу пользователя.

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
- Сохранять действующие светлые токены и структуру согласованного макета.
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

1. Проверить текущую React-страницу и согласованный план.
2. Найти связанные JS-модули и API endpoints.
3. Сохранить исключённые пользователем страницы и темы.
4. Создать или обновить только минимальный набор React-файлов.
5. Добавить типы и Zod-схемы для новых API-запросов.
6. Проверить текущие React-маршруты и сборку.

## Запрещённые действия без явного запроса

- Вручную редактировать сгенерированные `wwwroot/*.html`.
- Вручную редактировать сгенерированные `wwwroot/assets/*`.
- Возвращать legacy CSS/JS в публикацию.
- Переключать все routes на React сразу.
- Менять auth storage keys.
- Менять значения статусов рекомендаций.
- Делать `git commit`.

## Хороший промпт для генерации

```text
Измени только страницу <имя страницы> в React ClientApp.
В wwwroot находится только генерируемая сборка.
Используй React + TypeScript + Vite + MUI + TanStack Query + Zod.
Сохрани текущие API endpoints и localStorage keys.
Добавь минимальные компоненты и типы.
Не добавляй Redux, Next.js, Tailwind.
После изменений перечисли изменённые файлы и риски.
```

