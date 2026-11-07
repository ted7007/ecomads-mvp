# Первый источник регистрации

Лендинг `https://ecomads.ru/site` и регистрация `/register` используют один origin.
Кнопки «Начать бесплатно» ведут на `/register`. Источник сохраняется в localStorage
под ключом `ecomads.firstAttribution` на 30 суток с первого валидного перехода.
Пример ссылки — `https://ecomads.ru/site?utm_source=telegram_post_1`.
Формат источника — 1–128 символов из `A–Z`, `a–z`, `0–9`, `.`, `_`, `-`.

Регистрация передаёт необязательный объект `attribution` с `utm_source` и
`captured_at_utc` в формате ISO 8601 UTC с `Z`. Бэкенд повторно проверяет метку
и её возраст и сохраняет данные при создании `Seller`. Источник аккаунта
хранится бессрочно. Повреждённая или просроченная атрибуция игнорируется.
Клиентский timestamp служит проверкой срока хранения, его подлинность не заверяется.

Начальная атрибуция и просмотр Метрики выполняются до создания роутера.
Исходный URL передаётся Метрике полностью, включая метку. Редирект Caddy
с `www.ecomads.ru` сохраняет URI и query. При заблокированном localStorage
источник сохраняется в памяти текущей страницы, до её перезагрузки.

## Проверка в PostgreSQL

Миграция `AddRegistrationAttribution` добавляет nullable-поля `utm_source`
и `utm_captured_at_utc` в `sellers`. В существующем psql-сеансе выполнить
следующий запрос, подставив email тестовой регистрации через переменную psql.

```sql
\set email 'test@example.com'
SELECT id, email, created_at, utm_source, utm_captured_at_utc
FROM sellers
WHERE email = :'email';
```

## Отчёт по источникам

Успешным импортом считается завершённое задание WB с загруженными строками.
Каждый пользователь учитывается один раз, независимо от количества заданий
и кабинетов. Окно начинается в момент регистрации и заканчивается через
семь суток, исключая правую границу. `NULL` объединяет аккаунты без источника.
Для пользователей младше семи суток показатель ещё накапливается.
Связь с импортами доступна, пока соответствующие кабинеты и задания сохранены в БД.

```sql
SELECT
    s.utm_source,
    COUNT(*) AS registered_users,
    COUNT(*) FILTER (
        WHERE EXISTS (
            SELECT 1
            FROM stores st
            JOIN wb_sync_jobs j ON j.store_id = st.id
            WHERE st.seller_id = s.id
              AND j.status = 'completed'
              AND j.imported_rows > 0
              AND j.completed_at_utc >= s.created_at
              AND j.completed_at_utc < s.created_at + INTERVAL '7 days'
        )
    ) AS users_with_import_7d
FROM sellers s
GROUP BY s.utm_source
ORDER BY registered_users DESC;
```

## Автоматические проверки

Из `Ecomads.WebApplication/ClientApp` запустить
`node --test tests/attribution.test.mjs tests/returnTo.test.mjs` и `npm run build`.
Из корня проекта запустить
`dotnet test Ecomads.WebApplication.Tests --filter FullyQualifiedName~RegistrationAttributionTests`.
Интеграционные тесты используют PostgreSQL через существующий Testcontainers
или `ECOMADS_TEST_POSTGRES` с правом создавать отдельные тестовые БД.
