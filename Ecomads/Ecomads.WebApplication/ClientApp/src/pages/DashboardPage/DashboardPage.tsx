import RefreshIcon from '@mui/icons-material/Refresh';
import StorefrontIcon from '@mui/icons-material/Storefront';
import { Alert, Button, Card, CardContent, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { appRoutes } from '../../app/routes';
import { queryKeys } from '../../shared/api/queryKeys';
import type { DashboardFilters } from './dashboardApi';
import { getCampaigns, getDailySeries, getLoadedPeriods } from './dashboardApi';
import { CampaignsTable } from './components/CampaignsTable';
import { DashboardKpiGrid } from './components/DashboardKpiGrid';
import { PeriodFilter, defaultPeriod } from './components/PeriodFilter';
import { ErrorState } from '../../shared/ui/ErrorState';
import { LoadingState } from '../../shared/ui/LoadingState';
import { PageHeader } from '../../shared/ui/PageHeader';
import { DailyChart } from '../../shared/ui/DailyChart';
import { RecommendationList } from '../../shared/ui/RecommendationList';
import { campaignRecommendations } from '../../shared/lib/recommendations';

export function DashboardPage() {
  const location = useLocation();
  const navigate = useNavigate();
  const [filters, setFilters] = useState<DashboardFilters>(defaultPeriod);
  const [draftFilters, setDraftFilters] = useState<DashboardFilters>(defaultPeriod);

  const campaignsQuery = useQuery({
    queryKey: queryKeys.projects.list(filters),
    queryFn: () => getCampaigns(filters)
  });

  const periodsQuery = useQuery({
    queryKey: queryKeys.statistics.periods,
    queryFn: getLoadedPeriods
  });
  const dailyQuery = useQuery({ queryKey: ['dashboard-daily', filters], queryFn: () => getDailySeries(filters) });

  const campaigns = campaignsQuery.data ?? [];
  const campaignsWithData = campaigns.filter((campaign) => campaign.kpi.coverageDays > 0).length;
  const hasIncompleteDays = campaigns.some((campaign) =>
    campaign.kpi.coverageDays > 0 && campaign.kpi.coverageDays < campaign.kpi.expectedDays);
  const recommendations = campaignRecommendations(campaigns);
  const demoFeedbackSuccess = (location.state as { demoFeedbackSuccess?: string } | null)?.demoFeedbackSuccess;

  return (
    <Stack spacing={3}>
      <PageHeader
        title="Сводка"
        actions={
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
            <Button
              color="primary"
              startIcon={<RefreshIcon />}
              variant="outlined"
              onClick={() => {
                void campaignsQuery.refetch();
                void periodsQuery.refetch();
                void dailyQuery.refetch();
              }}
            >
              Обновить
            </Button>
            <Button startIcon={<StorefrontIcon />} variant="contained" onClick={() => navigate(appRoutes.wbStores)}>
              Кабинеты WB и сбор данных
            </Button>
          </Stack>
        }
      />

      {demoFeedbackSuccess ? <Alert severity="success">{demoFeedbackSuccess}</Alert> : null}

      <Card>
        <CardContent>
          <PeriodFilter
            draftFilters={draftFilters}
            periods={periodsQuery.data ?? []}
            onApply={(next) => setFilters({ startDate: next.startDate || undefined, endDate: next.endDate || undefined })}
            onDraftChange={(nextFilters) => setDraftFilters(nextFilters)}
          />
        </CardContent>
      </Card>

      {campaignsQuery.isLoading ? <LoadingState title="Загружаем обзор рекламы" /> : null}

      {campaignsQuery.isError ? (
        <ErrorState
          title="Не удалось загрузить обзор рекламы"
          description={campaignsQuery.error instanceof Error ? campaignsQuery.error.message : 'Проверьте соединение и авторизацию.'}
          onRetry={() => void campaignsQuery.refetch()}
        />
      ) : null}

      {!campaignsQuery.isLoading && !campaignsQuery.isError ? (
        <>
          {campaigns.length > 0 && campaignsWithData < campaigns.length ?
            <Alert severity="warning">Статистика за период есть по {campaignsWithData} из {campaigns.length} кампаний. Сводные показатели относятся только к загруженным кампаниям, а не ко всему кабинету.{hasIncompleteDays ? ' У некоторых кампаний загружены не все дни.' : ''}</Alert> :
            hasIncompleteDays ? <Alert severity="warning">У некоторых кампаний загружены не все дни. Сводные показатели за период неполные.</Alert> : null}
          <Card>
            <CardContent>
              <Stack spacing={1.5}>
                <Typography variant="h6" fontWeight={800}>Требуют внимания {recommendations.length ? `· ${recommendations.length}` : ''}</Typography>
                <RecommendationList items={recommendations} startDate={filters.startDate} endDate={filters.endDate} emptyText={campaigns.length === 0 ?
                  'Кампаний за выбранный период пока нет. Подключите WB и загрузите данные.' :
                  'Подтверждённых превышений рекламного ДРР нет. Кампании с неполным периодом или без рекламной суммы заказов не оцениваются.'} />
              </Stack>
            </CardContent>
          </Card>

          <DashboardKpiGrid campaigns={campaigns} />

          <Card><CardContent>
            {dailyQuery.isError ? <Alert severity="error">Не удалось загрузить дневную динамику.</Alert> :
              <DailyChart days={dailyQuery.data ?? []} />}
          </CardContent></Card>

          <Card>
            <CardContent>
              <Stack spacing={2}>
                <Typography variant="h6" fontWeight={800}>
                  Рекламные кампании
                </Typography>
                <CampaignsTable campaigns={campaigns} filters={filters} />
              </Stack>
            </CardContent>
          </Card>
        </>
      ) : null}

    </Stack>
  );
}
