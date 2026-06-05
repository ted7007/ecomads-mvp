# Proposed frontend structure

Рекомендуемая структура для нового React-фронтенда.

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
          errors.ts
        auth/
          tokenStorage.ts
          authTypes.ts
          RequireAuth.tsx
        ui/
          AppLayout.tsx
          Sidebar.tsx
          KpiCard.tsx
          DataTable.tsx
          EmptyState.tsx
          LoadingState.tsx
          ErrorState.tsx
        lib/
          formatMoney.ts
          formatPercent.ts
          formatDate.ts
      pages/
        LoginPage/
          LoginPage.tsx
          loginSchema.ts
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
        DashboardPage/
          DashboardPage.tsx
          dashboardApi.ts
          dashboardSchemas.ts
          components/
            CampaignsTable.tsx
            DashboardKpiGrid.tsx
            PeriodFilter.tsx
        CampaignPage/
          CampaignPage.tsx
          campaignApi.ts
          campaignSchemas.ts
          components/
            CampaignKpiGrid.tsx
            KeywordStatsTable.tsx
            RecommendationsPanel.tsx
      features/
        statisticsUpload/
          UploadStatisticsDialog.tsx
          uploadStatisticsApi.ts
          uploadStatisticsTypes.ts
        recommendations/
          recommendationApi.ts
          recommendationSchemas.ts
          RecommendationActions.tsx
```

## Разделение ответственности

`app`

- Инициализация приложения.
- Router.
- Providers.
- MUI theme.

`shared`

- Переиспользуемые UI-компоненты.
- API infrastructure.
- Auth/token helpers.
- Форматтеры.
- Не должен импортировать `pages` или `features`.

`pages`

- Компоненты страниц.
- Page-specific API, schemas, types.
- Page-specific layout composition.

`features`

- Самостоятельные бизнес-виджеты, которые нужны на нескольких страницах.
- Например upload статистики и рекомендации.

## Минимальный routing

```text
/app/login
/app/report
/app/dashboard
/app/campaign/:campaignId
```

На первом этапе можно оставить legacy links:

```text
/index.html
/report.html
/dashboard.html
/campaign.html?id=...
```

React routes подключать по одному, без массового переключения навигации.

## MUI theme

Тему MUI нужно приблизить к текущему CSS:

- `primary` — текущий accent.
- `error` — текущий danger.
- `success` — текущий success.
- `warning` — текущий warning.
- `fontFamily` — `Inter`.
- Card radius и shadows — близко к `wwwroot/css/style.css`.

Не нужно сразу переносить весь CSS. Достаточно воспроизвести визуально важные токены.

