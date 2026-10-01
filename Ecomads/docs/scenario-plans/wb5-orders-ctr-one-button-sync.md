# WB-5: все заказы, норма CTR и загрузка WB одной кнопкой

Источник: запрос пользователя 2026-10-02. Продолжение `wb4-light-visual-iteration-2.md` и `light-ui-and-sync-visibility.md`. План написан для пошагового исполнения: каждый этап — отдельный коммит, этапы выполнять **по порядку** (в этапах 1, 2, 3, 5 есть миграции EF, параллельная работа даст конфликтующие снимки модели).

## Статус исполнения 2026-10-02

| Этап | Статус | Коммит |
| --- | --- | --- |
| 1. Норма CTR | Готово | `932ca03` |
| 2. Лимиты по методам | Готово | `1e60a47` |
| 3. Все заказы из воронки | Готово | `0088acc` |
| 4. Заказы и ДРР в сводке | Готово | `2c7e748` |
| 5. Загрузить всё | Готово | `6fd4862` |
| 6. Экран кабинета WB | Готово | `4084426` |
| 7. Автообновление за флагом | Готово, флаг выключен | `2e76d41` |
| 8. Дозагрузка старой истории | Готово | `44eba9f` |
| 9. Документы и публикация | Опубликовано и проверено под входом; ДРР зависит от полного покрытия рекламной статистики | текущий коммит |

После этапа 8 backend и frontend собираются, все 26 интеграционных тестов проходят. Дозагрузка охватывает отсутствующие дни последних 30 дней; для сравнения с более ранним периодом история продолжает накапливаться при ежедневных загрузках.

Публикация: образ `ecomads:20261001T165842Z-44eba9f-dirty` запущен через `ops/deploy.ps1`. Скрипт создал резервную копию БД и успешно проверил `/health`; журнал контейнера подтвердил миграции `AddMinCtrNorm`, `PerKindWbSyncSlots`, `AddWbStoreDailyOrders`, `AddWbRefreshRuns`.

Проверка на опубликованном сайте под входом в ранее созданный тестовый кабинет: одно нажатие «Загрузить всё из WB» поставило задания всех четырёх источников. Статистика кампаний, кластеры и Джем завершились; воронка загрузила свежие 7 дней и продолжает дозагрузку старой истории по лимиту WB (1 из 13 запросов на момент проверки). «Заказы» за 7 дней заполнены и их линия включается на графике. «ДРР от заказов» показывает «—» с причиной «рекламная статистика загружена не полностью»: в сводке покрыты 18 из 36 кампаний, хотя запрос статистики обработал все 36; подменять неполный расход точным ДРР нельзя. Карточка CTR кампании показывает «норма 3%». Экраны кабинета WB и сводки проверены на 1440 и 390 px, горизонтального переполнения нет; на 390 px кнопка занимает ширину карточки.

## Что нужно получить

1. В сводке работают «Заказы» (сумма всех заказов кабинета, включая нерекламные) и «ДРР от заказов» (расход рекламы / все заказы × 100). Источник — воронка продаж WB, а не рекламная статистика.
2. В продукте есть норма CTR (по умолчанию 3%). На карточке CTR кампании показано «норма X%», значение ниже нормы окрашено как предупреждение.
3. Загрузка из WB запускается одной кнопкой «Загрузить всё из WB»: без выбора периода и без ввода ID кампаний. Система сама загружает статистику кампаний, все заказы, кластеры и Джем в пределах лимитов WB. Инфраструктура готова к ежедневному автозапуску, который включается флагом.

## Факты о лимитах WB (проверено по OpenAPI 2026-10-02)

Спецификации: `https://dev.wildberries.ru/api/swagger/yaml/ru/08-promotion.yaml` и `.../11-analytics.yaml`. Страницы документации отдают 498 для ботов, YAML скачивается напрямую. Лимиты указаны **для каждого метода отдельно**, на один аккаунт продавца.

| Метод | Kind в EcomAds | Базовый токен | Персональный, сервисный, базовый с секретом | Объём одного запроса |
| --- | --- | --- | --- | --- |
| `GET /api/advert/v2/adverts` | — (список кампаний) | 1 в час | 5 в секунду | все кампании |
| `GET /adv/v3/fullstats` | `fullstats` | 1 в час | 3 в минуту | до 50 кампаний, до 31 дня |
| `POST /adv/v1/normquery/stats` | `clusters` | 2 в час (интервал 30 мин) | 10 в минуту | до 100 пар «кампания–товар», до 7 дней |
| `POST /api/v2/search-report/product/search-texts` | `jam` | 1 в час | 3 в минуту | до 50 товаров, до 7 дней |
| `POST /api/analytics/v3/sales-funnel/grouped/history` | `funnel` (новый) | 2 в час (интервал 30 мин) | 3 в минуту | все карточки, **только последние 7 дней**, по дням |
| `POST /api/analytics/v3/sales-funnel/products` | `funnel` (дозагрузка) | 2 в час (интервал 30 мин) | 3 в минуту | период + прошлый период, до 1000 карточек на страницу, не старше 365 дней |

Выводы:

- Сейчас `WbSyncWorker` и контроллеры держат **один общий часовой слот на все методы** и только одно активное задание на кабинет. Это строже, чем требует WB. Если перейти на слоты по методам, за один запуск всё укладывается в текущие лимиты, и **повышать лимиты не нужно**.
- При базовом токене кабинет до 50 кампаний, 100 пар и 50 товаров загружается за несколько минут, если слоты свободны. Каждые следующие 50 кампаний добавляют 1 час, 100 пар — 30 минут, 50 товаров Джема — 1 час. С персональным токеном всё укладывается в минуты.
- Дневная воронка доступна только за последние 7 дней. История «Заказов» копится с каждой ежедневной загрузкой. Старые дни можно дозагрузить методом `products`: два дня за запрос, 30 дней примерно за 6 часов (этап 8).
- Тип токена из JWT не определяем. Интервалы берём для базового токена, на 429 соблюдаем `X-Ratelimit-Retry`/`Retry-After` (уже реализовано в `WbPromotionClient.GetRetryAfter`).

## Общие правила для исполнителя

- Рабочая папка: `C:\Work\ecomads-mvp\Ecomads`. Backend: `Ecomads.WebApplication`. Frontend: `Ecomads.WebApplication/ClientApp` (React 18, MUI 6, TanStack Query, zod). Тесты: `Ecomads.WebApplication.Tests` (нужен Docker, используются Testcontainers PostgreSQL).
- Проверки после каждого этапа: `dotnet build Ecomads.sln`, `dotnet test Ecomads.sln`, `npm run build` в `ClientApp`. Этап не считается готовым, пока они не зелёные.
- Миграции: `dotnet ef migrations add <Имя> --project Ecomads.WebApplication`. Если `dotnet ef` не установлен: `dotnet tool install --global dotnet-ef`. Последняя существующая миграция — `20260930041959_AddWbSyncVisibility`. Имена колонок — snake_case через `HasColumnName`, как в `EcomadsDbContext`.
- Не подставлять демо-значения, не выдавать рекламные заказы за все заказы. Нет данных — показывать «—» и причину.
- Токены WB, ответы WB и `.env*` не коммитить и не выводить в логи.
- Тёмная тема вне рамок. Стиль кода — как в соседних файлах; комментарии только для неочевидных ограничений.
- Не менять поведение страниц, которых этап не касается.

---

## Этап 1. Норма CTR

Независимый этап, удобен для разгона.

Backend:

- `Data/Models/WbNormSettings.cs`: в `WbStoreNorms` добавить `public decimal MinCtr { get; set; } = 3m;`, в `WbCampaignNorms` — `public decimal? MinCtr { get; set; }`.
- `Data/EcomadsDbContext.cs`: `min_ctr`, `decimal(8,2)` в обеих таблицах; для `wb_store_norms` задать `HasDefaultValue(3m)`.
- Миграция `AddMinCtrNorm`.
- `Controllers/WbNormsController.cs`:
  - добавить `MinCtr` в `StoreNormRequest` (decimal) и `CampaignNormRequest` (decimal?) последним параметром;
  - сохранять его в обоих `Put`, включить в fallback (`?? 3m`) и в `effective`;
  - валидация: `0 < MinCtr <= 100`, для кампании `null` допустим.
  - Если ревизии норм (`WbNormRevision.SettingsJson`) сериализуют request, поле попадёт туда само; проверить.
- `Models/ProjectDashboardDto.cs`: в `ProjectDashboardDto` добавить `public decimal MinCtr { get; init; } = 3m;`.
- `Controllers/ProjectsController.cs`: в блок `targets` добавить `CampaignMinCtr` и `StoreMinCtr` по аналогии с `TargetDrr`; в `with` выставить `MinCtr = CampaignMinCtr ?? StoreMinCtr ?? 3m`.

Frontend:

- `pages/NormsPage/normsApi.ts`: добавить `minCtr` в zod-схемы values и overrides (overrides — `nullable`).
- `pages/NormsPage/NormsPage.tsx`: поле «Минимальный CTR, %» рядом с целевым ДРР, в той же группе порогов и для кабинета, и для кампании (пустое у кампании означает «наследуется»).
- `shared/api/apiSchemas.ts` и `apiTypes.ts`: `minCtr: z.number()` в схеме проекта.
- `pages/CampaignPage/components/CampaignKpiGrid.tsx`, карточка CTR:
  - `note={`норма ${formatPercent(campaign.minCtr)}`}`, тем же форматом, что «норма X%» у ДРР;
  - `valueColor="warning.main"` при `ctr < minCtr` и наличии данных;
  - `delta`/`deltaTone` оставить; `previous` («было …») убрать: в макете на этом месте норма.

Тесты: в `Integration/WbWorkflowE2ETests.cs` (или рядом) — PUT норм кабинета с `minCtr: 2.5`, GET `/api/projects` возвращает `minCtr = 2.5`; переопределение кампании `minCtr: 4` имеет приоритет. Невалидное `minCtr: 0` даёт 400.

Готово, когда: карточка CTR кампании показывает «норма 3%» по умолчанию, значение меняется после правки на странице норм, низкий CTR оранжевый.

---

## Этап 2. Лимиты по методам вместо общего слота

Цель: разные методы WB не ждут друг друга; одновременно может идти по одному заданию **каждого вида** на кабинет.

- Новый файл `Services/Wb/WbRateLimits.cs`:

```csharp
public static class WbRateLimits
{
    public static TimeSpan IntervalFor(string kind) => kind switch
    {
        "clusters" or "funnel" => TimeSpan.FromMinutes(30),
        _ => TimeSpan.FromHours(1) // fullstats, jam
    };

    public static async Task<DateTime> NextSlotAsync(EcomadsDbContext db, Guid storeId, string kind,
        DateTime now, CancellationToken cancellationToken)
    {
        var last = await db.WbSyncJobs.Where(x => x.StoreId == storeId && x.Kind == kind && x.LastRequestAtUtc != null)
            .MaxAsync(x => x.LastRequestAtUtc, cancellationToken);
        var next = last?.Add(IntervalFor(kind));
        return next > now ? next.Value : now;
    }
}
```

- Новый файл `Services/Wb/WbSyncJobUnits.cs` — единственное место расчёта объёма задания: `Total(WbSyncJob job)` (clusters — число пар, funnel — 1 или число единиц дозагрузки из этапа 8, остальные — длина `CampaignIdsJson`) и `Unit(string kind)` (`pair` / `product` / `request` / `campaign`). Заменить им дублирующиеся расчёты в `WbSyncWorker`, `WbStoresController.ToSyncResponse` и `WbSyncVisibilityController.View`.
- `WbSyncWorker.ProcessNextAsync`: `job.NextAttemptAtUtc = now.Add(WbRateLimits.IntervalFor(job.Kind))` вместо `FullStatsInterval`; константу удалить.
- `WbStoresController` (три `Start*`) и `WbSyncVisibilityController.Retry`:
  - `NextAttemptAtUtc` считать через `WbRateLimits.NextSlotAsync(db, storeId, kind, now, ct)`;
  - проверку активного задания сузить до того же `Kind`: есть активное того же вида — вернуть его (200), как сейчас для совпадающего вида. Конфликт 409 «другая загрузка» убрать;
  - для `Retry` проверять активное задание того же вида.
- `EcomadsDbContext`: уникальный индекс `HasIndex(e => e.StoreId).IsUnique().HasFilter(...)` заменить на `HasIndex(e => new { e.StoreId, e.Kind }).IsUnique().HasFilter("status IN ('pending', 'running')")`. Миграция `PerKindWbSyncSlots`.
- `WbSyncVisibilityController.Overview`: вместо одного `activeJob`/`blockedReason` вернуть в каждом `sources[]` поля `activeJob` и `blockedReason`. Верхнеуровневые `activeJob` (самое раннее активное) и `blockedReason` оставить для совместимости до этапа 6.
- `WbSyncVisibilityController.View`: добавить `estimatedCompletionAtUtc` для активных заданий: `NextAttemptAtUtc + (оставшиеся_запросы − 1) × IntervalFor(kind)`, где число оставшихся запросов = ceil(осталось единиц / размер пачки). Пачки: fullstats 50, clusters 100, jam 50, funnel 1. Размеры пачек вынести в `WbSyncJobUnits.BatchSize(kind)` и использовать в воркере.

Тесты:
- активное fullstats-задание не мешает поставить clusters, а второе fullstats возвращает существующее;
- `NextAttemptAtUtc` нового clusters-задания зависит только от прошлого запроса clusters (30 мин), а не от fullstats;
- обновить существующие тесты, которые ожидали 409.

Готово, когда: можно одновременно держать в очереди fullstats, clusters и jam одного кабинета, каждый со своим временем следующей попытки.

---

## Этап 3. Сбор всех заказов из воронки продаж

Хранение:

- Модель `Data/Models/WbStoreDailyOrders.cs`: `StoreId` (Guid), `Date` (DateOnly), `OrderCount` (int), `OrderSum` (decimal), `OpenCount` (int), `CartCount` (int), `BuyoutCount` (int), `BuyoutSum` (decimal), `Source` (string, `history` или `products`), `LoadedAtUtc` (DateTime).
- Таблица `wb_store_daily_orders`: ключ `(store_id, date)`, суммы `decimal(18,2)`, FK на `stores` с каскадом. `DbSet<WbStoreDailyOrders> WbStoreDailyOrders`. Миграция `AddWbStoreDailyOrders`.

Клиент:

- `Services/Wb/WbSalesFunnelClient.cs`: интерфейс `IWbSalesFunnelClient`, метод `Task<JsonDocument> GetGroupedHistoryAsync(string token, DateOnly start, DateOnly end, CancellationToken ct)`. Устроен как `WbJamClient`.
- Запрос: `POST /api/analytics/v3/sales-funnel/grouped/history`, Bearer-токен, тело:

```json
{ "selectedPeriod": { "start": "yyyy-MM-dd", "end": "yyyy-MM-dd" }, "brandNames": [], "subjectIds": [], "tagIds": [], "skipDeletedNm": false, "aggregationLevel": "day" }
```

- Проверки: период не длиннее 7 дней, иначе `ArgumentException`. Не 2xx — `WbApiException(status, WbPromotionClient.GetRetryAfter(response))`.
- Ответ: `{ "data": [ { "product": {...}, "history": [ { "date", "orderCount", "orderSum", "openCount", "cartCount", "buyoutCount", "buyoutSum", ... } ], "currency": "RUB" } ] }`. Если `data` не массив, бросить `JsonException`.
- `Program.cs`: `AddHttpClient<IWbSalesFunnelClient, WbSalesFunnelClient>` с той же базой `https://seller-analytics-api.wildberries.ru` и таймаутом, что у `IWbJamClient`.

Импорт:

- `Services/Wb/WbSalesFunnelImporter.cs` (scoped, зарегистрировать в `Program.cs`):
  - суммирует `history` всех элементов `data` по дате (WB может вернуть несколько групп);
  - делает upsert в `wb_store_daily_orders` для каждой даты из запрошенного периода;
  - дни внутри периода, которых нет в ответе, записывает нулями: WB вернул период без заказов — это 0, а не пропуск;
  - элементы с `currency` не `RUB` пропускает и пишет предупреждение в лог без данных токена;
  - повторный импорт того же периода перезаписывает значения: WB дописывает данные в течение нескольких дней.

Задание:

- Kind `funnel`, `CampaignIdsJson = "[]"`, всего 1 единица. Период — 7 дней, заканчивая вчерашним днём по Москве (`yesterday.AddDays(-6)`..`yesterday`).
- `WbSyncWorker`: ветка `job.Kind == "funnel"` — вызов клиента, `Stage = "importing"`, импорт, `NextCampaignOffset = 1`.
- Ответ 403 или 401 для `funnel`: ошибка `wb_403`, задание падает. В UI текст: «В токене нет доступа к категории „Аналитика“».
- Временная точка запуска до этапа 5: `POST api/wb/stores/{id}/funnel/sync` без тела, по образцу `StartJamSync` с `WbRateLimits.NextSlotAsync`.
- `WbSyncVisibilityController`: добавить `"funnel"` в `Kinds`, имя «Все заказы (воронка продаж)» в `BlockedReason`, unit `request`.
- `SyncDashboard.tsx` и `wbStoresApi.ts`: принять kind `funnel` в zod-схемах и подписях (без новой кнопки — кнопки переделываются в этапе 6).

Тесты:
- фейковый `IWbSalesFunnelClient` в фабрике `WbWorkflowE2ETests`, ответ из двух групп;
- импорт суммирует группы, пустые дни записаны нулями, повторный импорт перезаписывает;
- 403 даёт `failed` и `wb_403`.

Готово, когда: после задания `funnel` в `wb_store_daily_orders` 7 строк за последние 7 дней.

---

## Этап 4. «Заказы» и «ДРР от заказов» в сводке и на графике

Backend, `Controllers/WbDailySeriesController.cs`:

- только при `campaignId == null` загрузить строки `WbStoreDailyOrders` по магазинам продавца с подключённым токеном (`ApiKey != null`) за период;
- в каждый день добавить `TotalOrderSum` (decimal?) и `TotalOrderCount` (int?). Значение не `null`, только если на эту дату есть строка у **каждого** подключённого магазина; иначе `null`. Для запроса по кампании оба поля всегда `null`;
- `TotalDrr` (decimal?) = `Spend / TotalOrderSum × 100`, если оба не `null` и `TotalOrderSum > 0`.
- `OrderedAmount` в `ProjectKpiDto` не трогать: на уровне кампании «всех заказов» нет, тест ожидает `null`.

Frontend:

- `pages/DashboardPage/dashboardApi.ts`: в `dailyPointSchema` добавить `totalOrderSum`, `totalOrderCount`, `totalDrr` — `z.number().nullable().optional()`, чтобы ответы кампании без полей проходили.
- `pages/DashboardPage/DashboardPage.tsx`: передать в `DashboardKpiGrid` дополнительно `days={dailyQuery.data ?? []}` и `previousDays={priorQuery.data ?? []}`.
- `pages/DashboardPage/components/DashboardKpiGrid.tsx`:
  - «Заказы»: значение = сумма `totalOrderSum` за период, если у **всех** дней периода значение не `null`. Иначе «—» с note «загружено N из M дней» и подсказкой «История всех заказов копится с первой загрузки; WB отдаёт по дням только последние 7 дней». Направление сравнения +1. Сравнение с прошлым периодом — только если прошлый период тоже полный (как сделано для остальных KPI через `compareKpi`).
  - «ДРР от заказов» = (сумма расхода за период) / (сумма `totalOrderSum`) × 100, процентная единица, направление −1. Доступно при тех же условиях полноты и при полной рекламной статистике.
  - Обе карточки получают `onToggle` и переключают линии `orders` и `drrTotal`. Убрать `unavailableReason` и тексты «нет данных о всех заказах», если данные есть.
- `shared/ui/DailyChart.tsx`:
  - в `MetricKey` добавить `orders` (подпись «Заказы», цвет карточки `#1E7BF2`) и `drrTotal` (подпись «ДРР от заказов», бирюзовый цвет карточки);
  - значения дня — `totalOrderSum` и `totalDrr`; `null` даёт разрыв линии, как у остальных метрик. Своя шкала на метрику — по существующей логике;
  - в подсказке форматы: деньги для `orders`, проценты с одним знаком для `drrTotal`.
- Порядок карточек сводки не меняется: Заказы, Расход, ДРР от заказов, Заказы с рекламы, CTR.

Тесты: интеграционный тест дневного ряда — два дня с заказами и один без. Дни с данными дают сумму, день без строки — `null`; для `campaignId` поля `null`.

Готово, когда: при загруженной воронке за выбранный период «Заказы» и «ДРР от заказов» показывают числа и включают линии; при неполном периоде видна честная причина.

---

## Этап 5. Оркестрация «Загрузить всё»

Сервис:

- `Services/Wb/WbSyncPlanner.cs` (scoped). Перенести сюда логику подбора кампаний, пар и товаров из `StartSync`, `StartClusterSync`, `StartJamSync`; старые эндпоинты вызывают планировщик, их поведение и тесты сохраняются. Методы:
  - `EnqueueFullStatsAsync(store, runId, ct)`: последние 30 дней до вчера, кампании со статусами 9 и 11;
  - `EnqueueFunnelAsync(store, runId, ct)`;
  - `EnqueueClustersAsync(store, runId, ct)`: последние 7 дней, все наблюдаемые пары из завершённых fullstats;
  - `EnqueueJamAsync(store, runId, ct)`: последние 7 дней, все наблюдаемые товары; пропустить, если `store.JamStatus` равен `payment_required` или `access_denied`.
  - Каждый метод возвращает задание или `null` с причиной пропуска («нет кампаний», «нет данных о товарах», «нет подписки Джем»). Если активное задание того же вида уже есть, возвращает его, а не создаёт второе.
- `WbSyncJob`: поле `Guid? RunId` → колонка `run_id`, индекс `(store_id, run_id)`.
- `Store`: поле `DateTime? CampaignsRefreshedAtUtc` → `campaigns_refreshed_at_utc`. Миграция `AddWbRefreshRuns`.

Эндпоинт:

- `POST api/wb/stores/{storeId}/refresh` в `WbStoresController`, без тела:
  1. проверить владение и наличие токена;
  2. обновить список кампаний, если `CampaignsRefreshedAtUtc` старше часа или пуст. Логику upsert кампаний из `Connect` вынести в общий приватный метод или планировщик. На `WbApiException` (включая 429) не падать, а продолжить со старым списком и вернуть `campaignsRefreshed: false`;
  3. `runId = Guid.NewGuid()`, поставить `fullstats` и `funnel`;
  4. если завершённая fullstats-загрузка уже покрывает последние 7 дней, сразу поставить `clusters` и `jam`; иначе их поставит воркер после fullstats;
  5. вернуть `202` с `{ runId, campaignsRefreshed, jobs: [View...], skipped: [{ kind, reason }] }`.
- Если задания всех видов уже активны, вернуть `200` с ними (идемпотентно).

Воркер:

- в `WbSyncWorker.Complete` (или сразу после него), если `job.Kind == "fullstats" && job.RunId != null`, через `WbSyncPlanner` поставить `clusters` и `jam` с тем же `RunId`. Ошибку планирования записать в лог и не валить fullstats.

Тесты (фейковые клиенты WB):
- `refresh` создаёт fullstats и funnel с одним `RunId`;
- после завершения fullstats появляются clusters и jam;
- повторный `refresh` при активных заданиях не создаёт дублей;
- 429 при обновлении кампаний не мешает поставить задания.

Готово, когда: один POST без параметров приводит к загрузке всех четырёх источников.

---

## Этап 6. Экран «Кабинет WB»: одна кнопка

Файлы: `pages/WbStoresPage/WbStoresPage.tsx`, `SyncDashboard.tsx`, `wbStoresApi.ts`.

- Удалить поля «С даты», «По дату», «ID кампаний через запятую» и связанное состояние/валидацию (`campaignIds`, `startDate`, `endDate`, `invalidParameters`).
- Удалить кнопки «Загрузить» у каждого источника. Добавить одну основную кнопку «Загрузить всё из WB» (`variant="contained"`) → `POST /refresh`; после ответа обновить `sync-overview`.
- Кнопка неактивна, пока есть активные задания; рядом текст «Загрузка идёт, завершится примерно в HH:MM МСК» (максимум `estimatedCompletionAtUtc` по активным заданиям).
- Список источников в порядке: «Статистика кампаний», «Все заказы (воронка продаж)», «Поисковые кластеры», «Поисковые запросы Джема». В строке:
  - статус;
  - прогресс («12 из 30 кампаний»);
  - «следующий запрос в HH:MM — лимит WB»;
  - последняя успешная загрузка;
  - причина пропуска из ответа `refresh` (например, «нет подписки Джем»).
- Под кнопкой одной строкой: «Период выбирается автоматически: статистика кампаний — 30 дней, заказы, кластеры и Джем — 7 дней. Запросы идут в пределах лимитов WB».
- Журнал (`history`) и «Подробности» оставить свёрнутыми под «Журнал загрузок». В деталях вместо «ID кампаний: …» показывать число кампаний.
- `startWbSync`, `startWbClusterSync`, `startWbJamSync` из UI больше не вызываются; функции удалить из `wbStoresApi.ts`, эндпоинты на backend оставить.
- Проверить 1440 и 390 px: нет горизонтальной прокрутки, кнопка во всю ширину на 390.

Готово, когда: пользователь загружает всё одним нажатием и видит, что и когда догрузится.

---

## Этап 7. Ежедневное автообновление (за флагом)

- `Services/Wb/WbAutoRefreshWorker.cs` (`BackgroundService`), регистрация в `Program.cs`. Работает, только если `Wb:AutoRefresh:Enabled = true` в конфигурации. По умолчанию `false` в `appsettings.json`; включение — решение пользователя.
- Каждые 15 минут: для каждого магазина с `ApiKey != null` и не истёкшим `TokenExpiresAtUtc`:
  - если сейчас после 06:00 МСК;
  - и с 06:00 МСК сегодняшнего дня не было задания с `RunId`;
  - то вызвать тот же код, что `POST /refresh` (вынести его в `WbSyncPlanner.StartRefreshAsync(store, ct)`, контроллер вызывает его же).
- В UI под кнопкой при включённом флаге: «Автообновление каждый день после 06:00 МСК». Флаг отдавать в `GET api/wb/stores` (`autoRefreshEnabled`).
- Тест: при включённом флаге один проход создаёт run, второй проход в тот же день — нет.

---

## Этап 8. Дозагрузка старой истории заказов (по желанию)

Нужна, чтобы «Заказы» за 30 дней и сравнение с прошлым периодом работали сразу, а не через месяц накопления.

- В `IWbSalesFunnelClient` добавить `GetProductsAsync(token, DateOnly day, DateOnly pastDay, int offset, ct)`:
  - `POST /api/analytics/v3/sales-funnel/products`, тело `{ selectedPeriod: {day, day}, pastPeriod: {pastDay, pastDay}, nmIds: [], brandNames: [], subjectIds: [], tagIds: [], skipDeletedNm: false, limit: 1000, offset }`;
  - ответ `data.products[].statistic.selected.orderSum/orderCount` и `.past.orderSum/orderCount`.
- Kind `funnel` получает единицы работы в `CampaignIdsJson` как массив `yyyyMMdd` первых дней пар (или отдельный `PairIdsJson` по образцу clusters), плюс 1 единица для grouped/history.
- Дозагрузка охватывает дни от `yesterday − 29` до `yesterday − 7`, у которых нет строки в `wb_store_daily_orders`. Пагинация: продолжать, пока страница содержит 1000 карточек. Сумма по всем страницам — значение дня, `Source = "products"`.
- Стоимость на базовом токене: 2 дня за запрос (без пагинации) и 2 запроса в час, то есть примерно 4 дня в час; 23 дня — около 6 часов. Показать это в оценке времени.
- Тест на сумму по страницам и на то, что `history` не затирается `products` за последние 7 дней.

---

## Этап 9. Документы, публикация, проверка

- Отметить статусы в этом файле; в `wb3-wb4-design-gap.md` заменить фразу про недоступные «Заказы» и «ДРР от заказов» на фактическое состояние.
- Публикация: `ops/deploy.ps1` (делает резервную копию БД и проверяет `https://ecomads.ru/health`). Миграции применяются при старте приложения; убедиться в логах контейнера, что они прошли.
- Проверить на опубликованном сайте под реальным входом (пароль не запрашивать; если входа нет — честно указать ограничение):
  - нажать «Загрузить всё из WB» и дождаться заданий;
  - убедиться, что «Заказы» и «ДРР от заказов» заполнены за последние 7 дней;
  - карточка CTR кампании показывает «норма 3%»;
  - визуально проверить 1440 и 390 px.

## Вне рамок

Тёмная тема; бюджетные уведомления; определение типа токена; CSV-отчёты Джема за год (`DETAIL_HISTORY_REPORT`); загрузка статистики завершённых кампаний (статус 7) — поведение прежнее.
