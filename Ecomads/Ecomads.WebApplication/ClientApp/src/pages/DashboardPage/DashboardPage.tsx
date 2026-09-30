import { Accordion, AccordionDetails, AccordionSummary, Alert, Button, Card, CardContent, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { Link, useLocation, useSearchParams } from 'react-router-dom';
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
import type { MetricKey } from '../../shared/ui/DailyChart';
import { getWbStores } from '../WbStoresPage/wbStoresApi';
import { previousPeriod } from '../../shared/lib/previousPeriod';

export function DashboardPage() {
  const location = useLocation();
  const [searchParams, setSearchParams] = useSearchParams();
  const filters: DashboardFilters = { startDate: searchParams.get('startDate') ?? defaultPeriod().startDate,
    endDate: searchParams.get('endDate') ?? defaultPeriod().endDate };
  const [draftFilters, setDraftFilters] = useState<DashboardFilters>(filters);
  const [metric, setMetric] = useState<MetricKey>('revenue');
  useEffect(() => { setDraftFilters(filters); }, [filters.startDate, filters.endDate]);

  const campaignsQuery = useQuery({
    queryKey: queryKeys.projects.list(filters),
    queryFn: () => getCampaigns(filters)
  });

  const storesQuery = useQuery({ queryKey: ['wb-stores'], queryFn: getWbStores });
  const loadedPeriods = useQuery({ queryKey: ['loaded-periods'], queryFn: getLoadedPeriods });
  const dailyQuery = useQuery({ queryKey: ['dashboard-daily', filters], queryFn: () => getDailySeries(filters) });
  const prior = previousPeriod(filters);
  const priorQuery = useQuery({ queryKey: ['dashboard-daily', prior], queryFn: () => getDailySeries(prior) });

  const campaigns = campaignsQuery.data ?? [];
  const campaignsWithData = campaigns.filter((campaign) => campaign.kpi.coverageDays > 0).length;
  const hasIncompleteDays = campaigns.some((campaign) =>
    campaign.kpi.coverageDays > 0 && campaign.kpi.coverageDays < campaign.kpi.expectedDays);
  const recommendations = campaignRecommendations(campaigns);
  const demoFeedbackSuccess = (location.state as { demoFeedbackSuccess?: string } | null)?.demoFeedbackSuccess;

  const applyPeriod = (next: DashboardFilters) => {
    setSearchParams({ startDate: next.startDate ?? '', endDate: next.endDate ?? '' });
  };
  const dateLabel = `${new Date(`${filters.startDate}T00:00:00Z`).toLocaleDateString('ru-RU', { day: '2-digit', month: '2-digit', timeZone: 'UTC' })} – ${new Date(`${filters.endDate}T00:00:00Z`).toLocaleDateString('ru-RU', { day: '2-digit', month: '2-digit', timeZone: 'UTC' })}`;
  const incomplete = campaigns.length > 0 && (campaignsWithData < campaigns.length || hasIncompleteDays);
  const latestAvailable = [...(loadedPeriods.data ?? [])].sort((a, b) => b.endDate.localeCompare(a.endDate))[0];

  return (
    <Stack spacing={2.5}>
      <PageHeader
        title={`Сводка за ${dateLabel}`}
        description={`${storesQuery.data?.[0]?.name || 'Кабинет WB'} · сравнение с ${prior.startDate?.slice(5).split('-').reverse().join('.')} – ${prior.endDate?.slice(5).split('-').reverse().join('.')} · даты по МСК`}
        actions={
          <PeriodFilter draftFilters={draftFilters} periods={[]} onApply={applyPeriod} onDraftChange={setDraftFilters} />
        }
      />

      {demoFeedbackSuccess ? <Alert severity="success">{demoFeedbackSuccess}</Alert> : null}

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
          {campaignsWithData === 0 && latestAvailable ? <Alert severity="info" action={<Button onClick={() => applyPeriod(latestAvailable)}>Показать</Button>}>
            За выбранный период данных нет. Доступный период: {latestAvailable.startDate} – {latestAvailable.endDate}.
          </Alert> : null}
          {incomplete ? <Accordion disableGutters sx={{ '&:before': { display: 'none' } }}>
            <AccordionSummary sx={{ minHeight: 44, '& .MuiAccordionSummary-content': { my: 1 } }}><Typography variant="body2" color="warning.main">⚠ Данные неполные: {campaignsWithData} из {campaigns.length} кампаний. Подробнее</Typography></AccordionSummary>
            <AccordionDetails><Typography variant="body2">Показатели относятся только к загруженным кампаниям. {hasIncompleteDays ? 'Для части кампаний загружены не все дни.' : ''} Сбор можно проверить в разделе «Кабинет WB и Telegram».</Typography></AccordionDetails>
          </Accordion> : null}
          {recommendations.length ? <Card sx={{ '& .MuiCardContent-root': { py: 1.5 } }}>
            <CardContent>
              <Stack spacing={1.5}>
                <Typography variant="h6" fontWeight={800}>Требуют внимания · {recommendations.length}</Typography>
                <RecommendationList items={recommendations} maxVisible={3} startDate={filters.startDate} endDate={filters.endDate} emptyText="" />
              </Stack>
            </CardContent>
          </Card> : <Stack direction={{ xs: 'column', sm: 'row' }} alignItems={{ sm: 'center' }} gap={0.5} sx={{ px: 0.5 }}>
            <Typography variant="body2" color="text.secondary">
              {campaigns.length === 0 ? 'Кампаний за выбранный период пока нет.' :
                incomplete ? 'Рекомендаций пока нет: для подтверждённых выводов недостаточно данных.' : 'Отклонений от заданных норм нет.'}
            </Typography>
            {incomplete || campaigns.length === 0 ? <Button size="small" component={Link} to={appRoutes.wbStores} sx={{ px: 0.5, minWidth: 0, alignSelf: 'flex-start' }}>
              Проверить загрузку
            </Button> : null}
          </Stack>}

          <DashboardKpiGrid campaigns={campaigns} selectedMetric={metric} onSelect={setMetric} />

          <Card><CardContent>
            {dailyQuery.isError ? <Alert severity="error">Не удалось загрузить дневную динамику.</Alert> :
              <DailyChart days={dailyQuery.data ?? []} previousDays={priorQuery.data ?? []} metric={metric} onMetricChange={setMetric} />}
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
