import RefreshIcon from '@mui/icons-material/Refresh';
import { Alert, Breadcrumbs, Button, Card, CardContent, Chip, Link, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Navigate, useParams, useSearchParams } from 'react-router-dom';
import { appRoutes } from '../../app/routes';
import { queryKeys } from '../../shared/api/queryKeys';
import { ErrorState } from '../../shared/ui/ErrorState';
import { LoadingState } from '../../shared/ui/LoadingState';
import { PageHeader } from '../../shared/ui/PageHeader';
import { formatMoney } from '../../shared/lib/formatMoney';
import { formatPercent } from '../../shared/lib/formatPercent';
import { PeriodFilter, defaultPeriod } from '../DashboardPage/components/PeriodFilter';
import type { DashboardFilters } from '../DashboardPage/dashboardApi';
import { getCampaignPeriods, getCampaignSummary, getNomenclatureStatistics, getWbClusters, getWbJam, getWbSpendTrend } from './campaignApi';
import { CampaignKpiGrid } from './components/CampaignKpiGrid';
import { NomenclatureTable } from './components/NomenclatureTable';
import { WbClusterTable } from './components/WbClusterTable';
import { WbJamTable } from './components/WbJamTable';
import { getDailySeries } from '../DashboardPage/dashboardApi';
import { DailyChart } from '../../shared/ui/DailyChart';
import { RecommendationList } from '../../shared/ui/RecommendationList';
import { campaignRecommendations, clusterRecommendations } from '../../shared/lib/recommendations';

export function CampaignPage() {
  const { campaignId } = useParams();
  const [searchParams, setSearchParams] = useSearchParams();
  const initialFilters = (): DashboardFilters => ({
    startDate: searchParams.get('startDate') ?? defaultPeriod().startDate,
    endDate: searchParams.get('endDate') ?? defaultPeriod().endDate
  });
  const [filters, setFilters] = useState<DashboardFilters>(initialFilters);
  const [draftFilters, setDraftFilters] = useState<DashboardFilters>(initialFilters);
  const id = campaignId ?? '';
  const summary = useQuery({ queryKey: ['campaign-summary', id, filters],
    queryFn: () => getCampaignSummary(id, filters), enabled: Boolean(id) });
  const clusters = useQuery({ queryKey: ['wb-clusters', id, filters],
    queryFn: () => getWbClusters(id, filters), enabled: Boolean(id) });
  const jam = useQuery({ queryKey: ['wb-jam', id, filters],
    queryFn: () => getWbJam(id, filters), enabled: Boolean(id) });
  const articles = useQuery({ queryKey: ['campaign-nomenclatures', id, filters],
    queryFn: () => getNomenclatureStatistics(id, filters), enabled: Boolean(id) });
  const trend = useQuery({ queryKey: ['wb-spend-trend', id, filters.endDate],
    queryFn: () => getWbSpendTrend(id, filters.endDate), enabled: Boolean(id) });
  const daily = useQuery({ queryKey: ['campaign-daily', id, filters],
    queryFn: () => getDailySeries(filters, id), enabled: Boolean(id) });
  const periods = useQuery({ queryKey: queryKeys.statistics.periods, queryFn: getCampaignPeriods });

  if (!id) return <Navigate to={appRoutes.dashboard} replace />;
  const loading = summary.isLoading || clusters.isLoading || articles.isLoading;
  const error = summary.error ?? clusters.error ?? articles.error;

  return <Stack spacing={3}>
    <Breadcrumbs aria-label="Навигация"><Link href={appRoutes.dashboard} underline="hover">Сводка</Link>
      <Typography color="text.secondary">Рекламные кампании</Typography><Typography>{summary.data?.name ?? 'Кампания'}</Typography></Breadcrumbs>
    <PageHeader title={summary.data?.name ?? 'Кампания'} description={summary.data ?
      `Цель: ${summary.data.goal || 'не задана'} · целевой ДРР рекламы ${formatPercent(summary.data.targetDrr, 1)}` : undefined}
      actions={<Button startIcon={<RefreshIcon />}
      variant="outlined" onClick={() => { void summary.refetch(); void clusters.refetch(); void jam.refetch(); void articles.refetch(); void trend.refetch(); void daily.refetch(); }}>
      Обновить экран
    </Button>} />
    {summary.data?.wbStatus != null ? <Chip label={summary.data.wbStatus === 9 ? 'Активна' : summary.data.wbStatus === 11 ? 'Приостановлена' : `Статус WB: ${summary.data.wbStatus}`}
      color={summary.data.wbStatus === 9 ? 'success' : 'default'} variant="outlined" sx={{ alignSelf: 'flex-start' }} /> : null}
    <Card><CardContent><PeriodFilter draftFilters={draftFilters} periods={periods.data ?? []}
      onDraftChange={setDraftFilters} onApply={(selected) => {
        const next = { startDate: selected.startDate || undefined, endDate: selected.endDate || undefined };
        setFilters(next);
        const query = new URLSearchParams();
        if (next.startDate) query.set('startDate', next.startDate);
        if (next.endDate) query.set('endDate', next.endDate);
        setSearchParams(query);
      }} /></CardContent></Card>
    {loading ? <LoadingState title="Загружаем кампанию" /> : null}
    {error ? <ErrorState title="Не удалось загрузить кампанию"
      description={error instanceof Error ? error.message : 'Проверьте соединение.'}
      onRetry={() => { void summary.refetch(); void clusters.refetch(); void articles.refetch(); }} /> : null}
    {!loading && !error ? <>
      {summary.data && summary.data.kpi.coverageDays < summary.data.kpi.expectedDays ?
        <Alert severity="warning">Загружено {summary.data.kpi.coverageDays} из {summary.data.kpi.expectedDays} дней. Оценки по этому периоду предварительные.</Alert> : null}
      {summary.data && summary.data.kpi.coverageDays === summary.data.kpi.expectedDays &&
        summary.data.kpi.revenue > 0 && summary.data.kpi.drr > summary.data.targetDrr ?
        <Alert severity="warning">Рекламный ДРР {formatPercent(summary.data.kpi.drr, 1)} выше цели {formatPercent(summary.data.targetDrr, 1)} за выбранный период.</Alert> : null}
      <CampaignKpiGrid campaign={summary.data ?? null} />
      <Card><CardContent>{daily.isError ? <Alert severity="error">Не удалось загрузить дневную динамику.</Alert> :
        <DailyChart days={daily.data ?? []} title="Динамика кампании" />}</CardContent></Card>
      <Card><CardContent>
        <Typography variant="h6" fontWeight={800} sx={{ mb: 1 }}>Динамика расхода</Typography>
        {trend.isError ? <Alert severity="error">Не удалось загрузить сравнение расходов.</Alert> : null}
        {trend.data?.status === 'insufficient' ? <Typography color="text.secondary">
          Для сравнения нужны восемь завершённых дней подряд: выбранный день и семь предыдущих. Сейчас есть {trend.data.loadedDays}.
        </Typography> : null}
        {trend.data?.status === 'no_baseline' ? <Typography color="text.secondary">
          За предыдущие семь дней расход был нулевым; процентное отклонение не рассчитывается.
        </Typography> : null}
        {trend.data && !['insufficient', 'no_baseline'].includes(trend.data.status) ? <Typography color={
          trend.data.status === 'increase' ? 'warning.main' : 'text.primary'}>
          {formatMoney(trend.data.yesterdaySpend ?? 0)} за {trend.data.endDate} против среднего {formatMoney(trend.data.baselineDailySpend ?? 0)} за семь предыдущих дней
          {' '}({formatPercent(trend.data.changePercent ?? 0, 1)}). Порог отклонения: {formatPercent(trend.data.thresholdPercent, 0)}.
        </Typography> : null}
      </CardContent></Card>
      <Card><CardContent>
        <Typography variant="h6" fontWeight={800} sx={{ mb: 2 }}>Товары в кампании</Typography>
        <NomenclatureTable rows={articles.data ?? []} />
      </CardContent></Card>
      <Card><CardContent>
        <Typography variant="h6" fontWeight={800} sx={{ mb: 2 }}>Поисковые кластеры</Typography>
        {clusters.data?.isWbConnected && !clusters.data.isPeriodComplete ?
          <Alert severity="info" sx={{ mb: 2 }}>Сбор кластеров за весь выбранный период ещё не завершён. Строки ниже могут быть неполными; рекомендации по ним пока скрыты.</Alert> : null}
        {clusters.data?.isWbConnected ? <WbClusterTable rows={clusters.data.rows} />
          : <Alert severity="info">Подключите кабинет WB для просмотра кластеров.</Alert>}
      </CardContent></Card>
      <Card><CardContent>
        <Typography variant="h6" fontWeight={800} sx={{ mb: 1 }}>Рекомендации</Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Выводы по загруженным рекламным данным. Действия выполняются вручную в кабинете WB.
        </Typography>
        <RecommendationList startDate={filters.startDate} endDate={filters.endDate} items={[
          ...campaignRecommendations(summary.data ? [summary.data] : []),
          ...clusterRecommendations(id, clusters.data?.isPeriodComplete ? clusters.data.rows : [])
        ]} emptyText="Подтверждённых отклонений по выбранному периоду нет. Если данные неполные, выводы пока не делаем." />
      </CardContent></Card>
      <Card><CardContent>
        <Typography variant="h6" fontWeight={800} sx={{ mb: 2 }}>Поисковые запросы Джема</Typography>
        {jam.isLoading ? <LoadingState title="Загружаем запросы Джема" /> :
          jam.isError ? <Alert severity="error">Не удалось загрузить отчёт Джема.</Alert> :
          jam.data ? <WbJamTable report={jam.data} /> : null}
      </CardContent></Card>
    </> : null}
  </Stack>;
}
