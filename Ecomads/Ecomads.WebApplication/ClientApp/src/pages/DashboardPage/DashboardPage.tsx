import { Accordion, AccordionDetails, AccordionSummary, Alert, Button, Card, CardContent, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { Link, useLocation, useSearchParams } from 'react-router-dom';
import { appRoutes } from '../../app/routes';
import { queryKeys } from '../../shared/api/queryKeys';
import type { DashboardFilters } from './dashboardApi';
import { getCampaigns, getCoverage, getDailySeries, getLoadedPeriods } from './dashboardApi';
import { CampaignsTable } from './components/CampaignsTable';
import { DashboardKpiGrid } from './components/DashboardKpiGrid';
import { PeriodFilter, defaultPeriod, moscowToday } from './components/PeriodFilter';
import { ErrorState } from '../../shared/ui/ErrorState';
import { LoadingState } from '../../shared/ui/LoadingState';
import { PageHeader } from '../../shared/ui/PageHeader';
import { DailyChart } from '../../shared/ui/DailyChart';
import { ClusterRecommendationCard } from '../../shared/ui/ClusterRecommendationCard';
import { getRecommendationSummary } from '../../shared/api/clusterRecommendationApi';

import type { MetricKey } from '../../shared/ui/DailyChart';
import { getWbStores } from '../WbStoresPage/wbStoresApi';
import { previousPeriod } from '../../shared/lib/previousPeriod';
import { WbDataFreshness } from '../../shared/ui/WbDataFreshness';

export function DashboardPage() {
  const location = useLocation();
  const [searchParams, setSearchParams] = useSearchParams();
  const filters: DashboardFilters = { startDate: searchParams.get('startDate') ?? defaultPeriod().startDate,
    endDate: searchParams.get('endDate') ?? defaultPeriod().endDate };
  const [draftFilters, setDraftFilters] = useState<DashboardFilters>(filters);
  const [selectedMetrics, setSelectedMetrics] = useState<MetricKey[]>(['spend', 'drrTotal']);
  const [recommendationStore, setRecommendationStore] = useState('');
  const toggleMetric = (metric: MetricKey) => setSelectedMetrics((current) =>
    current.includes(metric) ? current.filter((item) => item !== metric) : [...current, metric]);
  useEffect(() => { setDraftFilters(filters); }, [filters.startDate, filters.endDate]);

  const campaignsQuery = useQuery({
    queryKey: queryKeys.projects.list(filters),
    queryFn: () => getCampaigns(filters)
  });

  const storesQuery = useQuery({ queryKey: ['wb-stores'], queryFn: getWbStores });
  const loadedPeriods = useQuery({ queryKey: ['loaded-periods'], queryFn: getLoadedPeriods });
  const dailyQuery = useQuery({ queryKey: ['dashboard-daily', filters], queryFn: () => getDailySeries(filters) });
  const coverageQuery = useQuery({ queryKey: ['wb-coverage', filters], queryFn: () => getCoverage(filters) });
  const includeToday = filters.endDate === moscowToday();
  const prior = previousPeriod(filters);
  const priorQuery = useQuery({ queryKey: ['dashboard-daily', prior], queryFn: () => getDailySeries(prior), enabled: !includeToday });
  const priorCoverageQuery = useQuery({ queryKey: ['wb-coverage', prior], queryFn: () => getCoverage(prior), enabled: !includeToday });
  const priorCampaignsQuery = useQuery({ queryKey: queryKeys.projects.list(prior), queryFn: () => getCampaigns(prior), enabled: !includeToday });

  const campaigns = campaignsQuery.data ?? [];
  const campaignsWithData = campaigns.filter((campaign) => campaign.kpi.coverageDays > 0).length;
  const hasIncompleteDays = campaigns.some((campaign) =>
    campaign.kpi.coverageDays > 0 && campaign.kpi.coverageDays < campaign.kpi.expectedDays);
  const recommendations = useQuery({ queryKey: ['wb-recommendation-summary', recommendationStore], queryFn: () => getRecommendationSummary(recommendationStore || undefined) });
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
        description={includeToday ? 'Предварительные показатели · даты по МСК' : `${storesQuery.data?.[0]?.name || 'Кабинет WB'} · сравнение с ${prior.startDate?.slice(5).split('-').reverse().join('.')} – ${prior.endDate?.slice(5).split('-').reverse().join('.')} · даты по МСК`}
        actions={
          <PeriodFilter draftFilters={draftFilters} periods={[]} onApply={applyPeriod} onDraftChange={setDraftFilters} />
        }
      />

      {demoFeedbackSuccess ? <Alert severity="success">{demoFeedbackSuccess}</Alert> : null}
      <WbDataFreshness startDate={filters.startDate!} endDate={filters.endDate!} />
      {Date.parse(`${filters.endDate}T00:00:00Z`) - Date.parse(`${filters.startDate}T00:00:00Z`) > 30 * 86400000 ?
        <Alert severity="info" action={<Button component={Link} to={appRoutes.wbStores}>Загрузить историю</Button>}>
          Для годовых графиков загрузите историю рекламы, расходов и заказов в кабинете WB. Заказы собираются по дням и могут загружаться несколько дней из-за лимита WB.
        </Alert> : null}

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
          <Stack spacing={0.75}>
          {(storesQuery.data?.length ?? 0) > 1 ? <TextField select label="Кабинет рекомендаций" size="small" value={recommendationStore} onChange={e => setRecommendationStore(e.target.value)} sx={{ maxWidth: 320 }}>
            <MenuItem value="">Все подключённые кабинеты</MenuItem>{storesQuery.data?.map(s => <MenuItem key={s.id} value={s.id}>{s.name}</MenuItem>)}
          </TextField> : null}
          {incomplete ? <Accordion disableGutters elevation={0} sx={{ bgcolor: 'rgba(255,255,255,.48)', border: '1px solid', borderColor: 'divider', boxShadow: 'none', '&:before': { display: 'none' } }}>
            <AccordionSummary sx={{ minHeight: 44, '& .MuiAccordionSummary-content': { my: 1 } }}><Typography variant="body2" color="warning.main">⚠ Данные неполные: {campaignsWithData} из {campaigns.length} кампаний. Подробнее</Typography></AccordionSummary>
            <AccordionDetails><Typography variant="body2">Показатели относятся только к загруженным кампаниям. {hasIncompleteDays ? 'Для части кампаний загружены не все дни.' : ''} Сбор можно проверить в разделе «Кабинет WB и Telegram».</Typography></AccordionDetails>
          </Accordion> : null}
          {recommendations.isLoading ? <Typography>Расчёт рекомендаций…</Typography> : null}
          {recommendations.isError ? <Alert severity="error">Не удалось получить рекомендации. <Button onClick={() => void recommendations.refetch()}>Повторить</Button></Alert> : null}
          {recommendations.data ? <Card><CardContent><Stack spacing={1.5}>
            <Typography variant="h6" fontWeight={800}>Требуют внимания · топ-5 рекомендаций</Typography>
            <Typography variant="body2">Рекомендации за 14 дней: {recommendations.data.window.startDate} — {recommendations.data.window.endDate}, МСК. Остатки и бюджет — по отдельным оперативным окнам.</Typography>
            {recommendations.data.top.map(({ runId, card }) => <Stack key={`${runId}:${card.evaluationId}:${card.rule.ruleId}`} spacing={.5}>
              <Button component={Link} sx={{ alignSelf: 'flex-start' }} to={`/campaign/${card.subject.campaignId}?startDate=${filters.startDate}&endDate=${filters.endDate}`}>Открыть кампанию</Button>
              <ClusterRecommendationCard runId={runId} card={card} />
            </Stack>)}
            {recommendations.data.remaining ? <Typography>Ещё {recommendations.data.remaining} рекомендаций — на экранах кампаний.</Typography> : null}
            {!recommendations.data.top.length ? <Typography variant="body2">{recommendations.data.enabled ? 'Карточек за расчётные 14 дней нет. Для отдельных кампаний данные могут быть неполными.' : 'Рекомендации отключены или нет подключённых кабинетов.'}</Typography> : null}
            {recommendations.data.campaigns.some(c => c.status !== 'no_deviations') ? <Accordion disableGutters elevation={0}>
              <AccordionSummary expandIcon="⌄"><Typography variant="body2">Итоги по кампаниям и ограничения данных</Typography></AccordionSummary>
              <AccordionDetails><Stack spacing={1}>{recommendations.data.campaigns.map(c => <Stack key={c.campaignId ?? c.name} spacing={.5}>
                <Typography variant="body2">{c.name}: {c.count} рекомендаций · {c.message}</Typography>
                {c.campaignId ? <Button size="small" component={Link} sx={{ alignSelf: 'flex-start' }} to={`/campaign/${c.campaignId}?startDate=${filters.startDate}&endDate=${filters.endDate}`}>Подробнее</Button> : null}
              </Stack>)}</Stack></AccordionDetails>
            </Accordion> : null}
          </Stack></CardContent></Card> : null}
          </Stack>

          <DashboardKpiGrid campaigns={campaigns} priorCampaigns={includeToday ? [] : priorCampaignsQuery.data ?? []}
            days={dailyQuery.data ?? []} previousDays={includeToday ? [] : priorQuery.data ?? []}
            coverage={coverageQuery.data ?? null} previousCoverage={includeToday ? null : priorCoverageQuery.data ?? null}
            selectedMetrics={selectedMetrics} onSelect={toggleMetric} />

          <Card><CardContent>
            {dailyQuery.isError ? <Alert severity="error">Не удалось загрузить дневную динамику.</Alert> :
              <DailyChart days={dailyQuery.data ?? []} previousDays={includeToday ? [] : priorQuery.data ?? []} selectedMetrics={selectedMetrics} storeLevel />}
          </CardContent></Card>

          <Card>
            <CardContent>
              <Stack spacing={2}>
                <Typography variant="h6" fontWeight={800}>
                  Рекламные кампании
                </Typography>
                <CampaignsTable campaigns={campaigns} priorCampaigns={includeToday ? [] : priorCampaignsQuery.data ?? []} filters={filters} />
              </Stack>
            </CardContent>
          </Card>
        </>
      ) : null}

    </Stack>
  );
}
