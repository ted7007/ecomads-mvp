# Task 05: demo access and product analytics MVP

## Цель

Встроить в текущий EcomAds MVP-механизм демо-доступа на 3 дня, закрыть публичную регистрацию regular-пользователей до открытия продукта, собрать feedback после окончания demo и начать сохранять продуктовые метрики, включая LLM usage и затраты, если LLM была вызвана.

Это umbrella-plan. Реализацию вести последовательными задачами:

1. `05a-access-model-and-registration-gate` — модель доступа, миграция, создание demo-user и блокировка regular registration.
2. `05b-demo-access-enforcement` — сервис проверки доступа, middleware/filter/policy и redirect на feedback после истечения demo.
3. `05c-demo-feedback-flow` — сущность, страница и форма feedback, выдача `MvpAccess` после валидной обратной связи.
4. `05d-product-usage-analytics` — `ProductUsageEvent`, сервис аналитики, константы и трекинг продуктовых событий.
5. `05e-llm-usage-tracking-and-verification` — `LlmUsageEvent`, учет usage/cost, связь с product events, логи и тесты.

## Current product state

Проект уже умеет:

- загружать статистику из Excel-файла;
- показывать статистику в dashboard;
- сортировать статистику;
- показывать приоритетность ключей;
- показывать рекомендации;
- открывать рекомендации по конкретной карточке/ключу;
- показывать страницу ожидаемого эффекта.

Задача не должна переписывать продукт. Нужно аккуратно встроить demo-доступ и аналитику в текущую архитектуру.

## Required discovery before implementation

Перед кодовыми изменениями найти:

- где находится модель пользователя;
- как устроена авторизация;
- используется ли ASP.NET Identity;
- где создаются пользователи;
- где находится текущая регистрация и как закрыть создание regular-пользователя;
- где находятся endpoints dashboard;
- где находится загрузка статистики;
- где открываются рекомендации;
- где находится страница expected effect;
- где сейчас вызывается LLM/BotHub;
- как подключен DbContext;
- как создаются EF Core migrations;
- есть ли frontend JS для открытия рекомендации без перезагрузки страницы.

После анализа вывести краткий план изменений:

```text
1. Какие файлы будут изменены.
2. Какие сущности будут добавлены.
3. Какие миграции будут созданы.
4. Где будет стоять проверка demo-доступа.
5. Где будут трекаться события.
6. Где будет трекаться LLM usage.
```

## Task 05a: access model and registration gate

### Goal

Добавить признаки demo/MVP-доступа в модель пользователя, создать безопасную миграцию и закрыть текущую возможность публично зарегистрировать regular-пользователя до открытия продукта.

### Scope

Включить:

- `UserAccessType`:

```csharp
public enum UserAccessType
{
    Regular = 0,
    Demo = 1,
    MvpAccess = 2
}
```

- `DemoAccessStatus`:

```csharp
public enum DemoAccessStatus
{
    None = 0,
    Active = 1,
    ExpiredAwaitingFeedback = 2,
    FeedbackSubmitted = 3
}
```

- поля пользователя:

```csharp
public bool IsDemoUser { get; set; }
public UserAccessType AccessType { get; set; }
public DemoAccessStatus DemoStatus { get; set; }
public DateTime? DemoStartedAtUtc { get; set; }
public DateTime? DemoExpiresAtUtc { get; set; }
public DateTime? DemoFeedbackSubmittedAtUtc { get; set; }
public DateTime? MvpAccessGrantedAtUtc { get; set; }
```

- EF Core migration с безопасными дефолтами для существующих пользователей:

```csharp
IsDemoUser = false;
AccessType = UserAccessType.Regular;
DemoStatus = DemoAccessStatus.None;
DemoStartedAtUtc = null;
DemoExpiresAtUtc = null;
DemoFeedbackSubmittedAtUtc = null;
MvpAccessGrantedAtUtc = null;
```

- создание demo-user со значениями:

```csharp
IsDemoUser = true;
AccessType = UserAccessType.Demo;
DemoStatus = DemoAccessStatus.Active;
DemoStartedAtUtc = DateTime.UtcNow;
DemoExpiresAtUtc = DateTime.UtcNow.AddDays(3);
DemoFeedbackSubmittedAtUtc = null;
MvpAccessGrantedAtUtc = null;
```

- блокировку публичной регистрации regular-пользователей. До MVP наружу должны выдаваться только demo-доступы.

### Important rules

- Все даты хранить в UTC.
- Не ломать существующую авторизацию.
- Если используется ASP.NET Identity, расширить `ApplicationUser`.
- Если используется собственная модель пользователя, расширить ее.
- Не делать тарифную систему, оплату, биллинг или CRM.

### Acceptance criteria

- В модели пользователя есть признаки demo-user.
- Существующие пользователи после миграции продолжают работать как `Regular`.
- Можно создать demo-user с доступом на 3 дня.
- Публичная регистрация больше не создает regular-пользователя.

## Task 05b: demo access enforcement

### Goal

Добавить единый сервис состояния доступа и заблокировать основные страницы продукта после истечения demo без redirect loop и без влияния на login/logout/static files.

### Scope

Включить сервис:

```csharp
public interface IUserAccessService
{
    Task<UserAccessStateDto> GetAccessStateAsync(Guid userId);
    Task<bool> HasProductAccessAsync(Guid userId);
    Task<bool> ShouldRequireDemoFeedbackAsync(Guid userId);
    Task GrantMvpAccessAfterFeedbackAsync(Guid userId);
}
```

DTO:

```csharp
public class UserAccessStateDto
{
    public Guid UserId { get; set; }
    public bool IsDemoUser { get; set; }
    public UserAccessType AccessType { get; set; }
    public DemoAccessStatus DemoStatus { get; set; }
    public bool HasProductAccess { get; set; }
    public bool ShouldRequireDemoFeedback { get; set; }
    public DateTime? DemoStartedAtUtc { get; set; }
    public DateTime? DemoExpiresAtUtc { get; set; }
    public int? DemoDaysLeft { get; set; }
    public TimeSpan? DemoTimeLeft { get; set; }
}
```

Доступ:

- `Regular` всегда имеет доступ к продукту.
- `Demo` имеет доступ, пока `DemoExpiresAtUtc > DateTime.UtcNow`.
- Истекший demo без feedback не имеет доступа к основным страницам и редиректится на `/demo-feedback`.
- После feedback пользователь получает `MvpAccess`.
- `MvpAccess` имеет доступ к продукту.

### Routes to protect

- `/dashboard`;
- страницы кампаний;
- страница рекомендаций;
- страница ожидаемого эффекта;
- загрузка статистики.

### Routes to keep available

- login;
- logout;
- register или replacement page для закрытой регистрации;
- static files;
- `/demo-feedback`;
- API отправки feedback.

### UI for active demo

Если demo активно, в header/sidebar показать бейдж `Demo` и остаток:

```text
Осталось 3 дня
Осталось 2 дня
Остался 1 день
Осталось меньше 24 часов
```

Если осталось меньше 24 часов, показать предупреждение:

```text
Демо-доступ скоро закончится. После окончания нужно будет оставить обратную связь, чтобы продолжить пользоваться MVP.
```

### Acceptance criteria

- Demo-user до истечения 3 дней может пользоваться продуктом.
- Истекший demo-user не может открыть основные страницы продукта.
- Истекший demo-user редиректится на `/demo-feedback`.
- Нет бесконечного redirect loop.
- Static files, login и logout не блокируются.
- Regular и MvpAccess пользователи работают без изменений.

## Task 05c: demo feedback flow

### Goal

Добавить страницу `/demo-feedback`, сохранить feedback один раз на пользователя и выдавать `MvpAccess` после валидной обратной связи.

### Scope

Добавить сущность:

```csharp
public class DemoFeedback
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string GeneralComment { get; set; } = null!;
    public int DashboardClarityScore { get; set; }
    public int RecommendationsUsefulnessScore { get; set; }
    public string? WrongOrQuestionableRecommendations { get; set; }
    public string MostUsefulFeature { get; set; } = null!;
    public string? MissingForRegularUsage { get; set; }
    public string ContinueTestingAnswer { get; set; } = null!;
    public string WillingToPayAnswer { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
}
```

Добавить связь с пользователем, если текущая модель это поддерживает.

Добавить unique index:

```csharp
builder.Entity<DemoFeedback>()
    .HasIndex(x => x.UserId)
    .IsUnique();
```

### Page copy

На `/demo-feedback` показать:

```text
Демо-доступ закончился.

Оставьте обратную связь, чтобы получить доступ к MVP-версии.
```

### Form fields

Обязательные:

- общий комментарий, минимум 50 символов;
- понятность dashboard, оценка 1-5;
- полезность рекомендаций, оценка 1-5;
- самая полезная функция: `Dashboard`, `KeywordRecommendations`, `ExpectedEffectPage`, `StatisticsUpload`, `Other`;
- готовность продолжить MVP: `Да`, `Нет`, `Возможно`;
- готовность платить: `Да`, `Нет`, `Возможно`.

Необязательные:

- спорные или неправильные рекомендации;
- чего не хватило для регулярного использования.

### Validation

Feedback валиден, если:

```csharp
GeneralComment.Length >= 50
DashboardClarityScore >= 1 && DashboardClarityScore <= 5
RecommendationsUsefulnessScore >= 1 && RecommendationsUsefulnessScore <= 5
MostUsefulFeature is not null or empty
ContinueTestingAnswer is not null or empty
WillingToPayAnswer is not null or empty
```

Если feedback невалидный:

- не открывать MVP-доступ;
- показать ошибки;
- оставить пользователя на странице feedback.

Если feedback валидный:

- сохранить `DemoFeedback`;
- обновить пользователя:
  - `DemoFeedbackSubmittedAtUtc = DateTime.UtcNow`;
  - `DemoStatus = DemoAccessStatus.FeedbackSubmitted`;
  - `AccessType = UserAccessType.MvpAccess`;
  - `MvpAccessGrantedAtUtc = DateTime.UtcNow`;
- redirect на dashboard;
- показать success message:

```text
Спасибо за обратную связь. Доступ к MVP-версии открыт.
```

### Acceptance criteria

- `/demo-feedback` доступна авторизованному demo-user.
- Общий комментарий обязателен и минимум 50 символов.
- Валидный feedback сохраняется.
- После валидного feedback пользователь получает `MvpAccess`.
- Повторная отправка feedback не создает дубль.
- Если feedback уже отправлен, показывается сообщение и кнопка перехода в dashboard.

## Task 05d: product usage analytics

### Goal

Сохранять продуктовые события в БД без админки, агрегированных endpoints и влияния на пользовательские сценарии при ошибке записи метрик.

### Scope

Добавить сущность:

```csharp
public class ProductUsageEvent
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string EventName { get; set; } = null!;
    public string FeatureName { get; set; } = null!;
    public Guid? CampaignId { get; set; }
    public Guid? KeywordId { get; set; }
    public Guid? LlmUsageId { get; set; }
    public string? MetadataJson { get; set; }
    public string? Path { get; set; }
    public string? Method { get; set; }
    public string? UserAgent { get; set; }
    public string? IpHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
```

Добавить сервис:

```csharp
public interface IProductAnalyticsService
{
    Task TrackAsync(ProductUsageEventCreateDto dto);
}
```

Добавить `ProductUsageEventCreateDto` с полями из ТЗ.

Если сохранение события упало:

- залогировать ошибку;
- не ронять пользовательский сценарий.

### Constants

Добавить:

```csharp
public static class ProductEvents
{
    public const string StatisticsUploaded = "statistics_uploaded";
    public const string DashboardViewed = "dashboard_viewed";
    public const string KeywordRecommendationOpened = "keyword_recommendation_opened";
    public const string ExpectedEffectPageViewed = "expected_effect_page_viewed";
    public const string DemoFeedbackViewed = "demo_feedback_viewed";
    public const string DemoFeedbackSubmitted = "demo_feedback_submitted";
    public const string LlmRecommendationRequested = "llm_recommendation_requested";
    public const string LlmRecommendationGenerated = "llm_recommendation_generated";
    public const string LlmRecommendationFailed = "llm_recommendation_failed";
}
```

```csharp
public static class ProductFeatures
{
    public const string StatisticsUpload = "statistics_upload";
    public const string Dashboard = "dashboard";
    public const string KeywordRecommendations = "keyword_recommendations";
    public const string ExpectedEffectPage = "expected_effect_page";
    public const string DemoFeedback = "demo_feedback";
    public const string LlmRecommendations = "llm_recommendations";
}
```

### Events to track

- успешная загрузка Excel: `statistics_uploaded`;
- открытие dashboard: `dashboard_viewed`;
- открытие рекомендации по карточке/ключу: `keyword_recommendation_opened`;
- открытие страницы expected effect: `expected_effect_page_viewed`;
- открытие `/demo-feedback`: `demo_feedback_viewed`;
- успешная отправка feedback: `demo_feedback_submitted`.

Если рекомендация открывается на фронтенде без перезагрузки страницы, добавить JS tracking через backend endpoint аналитики.

### Safe metadata

Разрешено сохранять техническую и продуктовую метаинформацию:

```json
{
  "reportType": "...",
  "rowsCount": 1000,
  "fileSizeBytes": 123456,
  "campaignId": "..."
}
```

Не сохранять:

- содержимое Excel;
- полный список ключей;
- полный prompt;
- полный LLM response;
- токены;
- API keys;
- Authorization headers;
- пароли;
- персональные данные без необходимости.

IP хранить только как hash, если он нужен.

### Indexes

Добавить индексы для:

- `UserId`;
- `FeatureName`;
- `EventName`;
- `CreatedAtUtc`;
- `LlmUsageId`;
- `CampaignId`.

### Acceptance criteria

- ProductUsageEvent сохраняется для ключевых действий продукта.
- В БД можно понять, кто пользовался dashboard, загружал статистику, открывал рекомендации, смотрел expected effect и отправил feedback.
- Ошибка записи product analytics не ломает основной сценарий.
- Нет админской страницы аналитики.
- Нет endpoint агрегированной аналитики.

## Task 05e: LLM usage tracking and verification

### Goal

Сохранять usage/cost по каждому LLM-вызову, null-safe парсить BotHub/OpenAI-compatible usage, связывать LLM-затраты с продуктовыми событиями и закрыть задачу тестами/логами.

### Scope

Добавить сущность:

```csharp
public class LlmUsageEvent
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public Guid? CampaignId { get; set; }
    public Guid? KeywordId { get; set; }
    public string Provider { get; set; } = null!;
    public string Model { get; set; } = null!;
    public string OperationName { get; set; } = null!;
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
    public decimal? BothubCaps { get; set; }
    public decimal? EstimatedCostRub { get; set; }
    public bool IsSuccess { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public long DurationMs { get; set; }
    public string? RequestMetadataJson { get; set; }
    public string? ResponseMetadataJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
```

Добавить сервис:

```csharp
public interface ILlmUsageTrackingService
{
    Task<Guid?> TrackSuccessAsync(LlmUsageSuccessDto dto);
    Task<Guid?> TrackFailureAsync(LlmUsageFailureDto dto);
}
```

Добавить DTO из ТЗ.

Если сохранение `LlmUsageEvent` упало:

- залогировать ошибку;
- вернуть `null`;
- не ронять генерацию рекомендаций.

### Constants

Добавить:

```csharp
public static class LlmOperations
{
    public const string GenerateCampaignRecommendations = "generate_campaign_recommendations";
    public const string GenerateKeywordRecommendationExplanation = "generate_keyword_recommendation_explanation";
    public const string GenerateExpectedEffectExplanation = "generate_expected_effect_explanation";
}
```

Если сейчас LLM используется только для генерации рекомендаций, использовать `GenerateCampaignRecommendations`.

### BotHub/OpenAI-compatible usage

Проверить, возвращает ли текущий endpoint usage.

Если API поддерживает параметр, попробовать:

```csharp
bothub = new
{
    include_usage = true
}
```

Если параметр вызывает ошибку, убрать его и парсить стандартное поле `usage`, если оно приходит.

Usage парсить null-safe:

- `usage.prompt_tokens`;
- `usage.completion_tokens`;
- `usage.total_tokens`;
- `usage.bothub.caps`.

Если usage отсутствует:

- LLM-событие все равно сохранить;
- token/caps поля оставить `null`.

Если используется streaming:

- проверить финальный chunk;
- если usage не приходит, для MVP можно отключить streaming для генерации рекомендаций или оставить usage `null`.

### Product event linkage

Если пользователь нажал "Получить рекомендации" и реально произошел LLM-вызов:

1. Сохранить `llm_recommendation_requested`.
2. Выполнить LLM-запрос.
3. При успехе сохранить `LlmUsageEvent`, затем `llm_recommendation_generated` с `LlmUsageId`.
4. При ошибке сохранить failed `LlmUsageEvent`, затем `llm_recommendation_failed` с `LlmUsageId`.
5. Если рекомендация рассчитана без LLM, `LlmUsageId = null`, metadata содержит `{ "source": "deterministic" }`.

Не сохранять полный prompt, полный response, полный список ключей, Excel-файл, API key или Authorization header.

### Indexes

Добавить индексы для:

- `UserId`;
- `CampaignId`;
- `Provider`;
- `Model`;
- `OperationName`;
- `CreatedAtUtc`;
- `IsSuccess`.

### Logs

Добавить структурированные `ILogger`-логи для:

- создания demo-user;
- истечения demo-доступа;
- redirect на `/demo-feedback`;
- открытия feedback;
- успешной отправки feedback;
- выдачи MVP-доступа;
- успешной загрузки статистики;
- открытия dashboard;
- открытия рекомендации;
- открытия expected effect page;
- начала LLM-запроса;
- успешного LLM-запроса;
- ошибки LLM-запроса;
- ошибки сохранения product analytics event;
- ошибки сохранения LLM usage event.

Не логировать пароли, токены, API keys, Authorization headers, Excel content, полный prompt или полный response.

### Tests

Минимальные сценарии:

- Regular user имеет доступ к продукту.
- Demo user имеет доступ до истечения `DemoExpiresAtUtc`.
- Demo user после истечения редиректится на `/demo-feedback`.
- Demo user после истечения не может открыть dashboard.
- Demo user может открыть `/demo-feedback`.
- Feedback с комментарием меньше 50 символов отклоняется.
- Валидный feedback сохраняется.
- После валидного feedback пользователь получает `MvpAccess` и timestamps.
- Повторный feedback не создает дубль.
- ProductUsageEvent сохраняется при загрузке статистики, dashboard, рекомендации и expected effect.
- LlmUsageEvent сохраняется при успешном LLM-запросе.
- LlmUsageEvent сохраняется при неуспешном LLM-запросе.
- Если usage отсутствует в LLM response, событие сохраняется с null tokens.
- Ошибка сохранения product analytics не ломает основной сценарий.
- Ошибка сохранения LLM usage не ломает основной сценарий.

### Acceptance criteria

- LlmUsageEvent сохраняется для LLM-вызовов.
- Если LLM response содержит usage, сохраняются prompt tokens, completion tokens, total tokens и BotHub caps.
- Если usage отсутствует, LLM-событие сохраняется с null token/caps fields.
- ProductUsageEvent может быть связан с LlmUsageEvent через `LlmUsageId`.
- В БД можно понять, кто тратил LLM и сколько tokens/caps было потрачено на пользователя, кампанию и операцию.
- Ошибка записи LLM usage не ломает основной сценарий.
- Build и доступные тесты проходят.

## Out of scope

Не делать в этой задаче:

- админскую страницу аналитики;
- endpoint агрегированной аналитики;
- тарифы;
- оплату;
- полноценный биллинг;
- ограничение по количеству SKU;
- сложную CRM;
- email-рассылки;
- автоматическое создание demo-user через публичную форму, если этого нет в текущей архитектуре;
- полноценный BI-dashboard.

## Final acceptance criteria

Задача считается выполненной, если:

- в модели пользователя есть признаки demo-user;
- можно создать demo-user с доступом на 3 дня;
- demo-user до истечения 3 дней может пользоваться продуктом;
- после истечения demo-user не может пользоваться основными страницами продукта;
- после истечения demo-user отправляется на `/demo-feedback`;
- на `/demo-feedback` есть форма обратной связи;
- общий комментарий в feedback обязателен и минимум 50 символов;
- пользователь отвечает на обязательные вопросы;
- после валидного feedback пользователь получает доступ к MVP;
- повторный feedback не создает дубли;
- ProductUsageEvent сохраняется для upload, dashboard, recommendation open, expected effect, feedback view и feedback submit;
- LlmUsageEvent сохраняется для LLM-вызовов;
- usage/caps сохраняются, если они есть в LLM response;
- без usage LLM-событие сохраняется с null token/caps fields;
- ProductUsageEvent может быть связан с LlmUsageEvent через `LlmUsageId`;
- нет админской страницы аналитики;
- нет endpoint агрегированной аналитики;
- ошибка записи метрик не ломает пользовательский сценарий;
- существующие пользователи продолжают работать без изменений.

## Stop condition

На этом этапе не начинать реализацию. Следующий шаг перед кодом — изучить текущую структуру проекта по discovery checklist и вывести краткий план изменений.
