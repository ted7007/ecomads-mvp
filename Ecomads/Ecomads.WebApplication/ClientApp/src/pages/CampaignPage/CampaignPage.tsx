import { Alert, Breadcrumbs, Card, CardContent, Chip, Link, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
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
import type { MetricKey } from '../../shared/ui/DailyChart';
import { previousPeriod } from '../../shared/lib/previousPeriod';
import { completeKpi } from '../../shared/lib/kpiComparison';

export function CampaignPage() {
  const { campaignId } = useParams();
  const [searchParams, setSearchParams] = useSearchParams();
  const initialFilters = (): DashboardFilters => ({
    startDate: searchParams.get('startDate') ?? defaultPeriod().startDate,
    endDate: searchParams.get('endDate') ?? defaultPeriod().endDate
  });
  const [filters, setFilters] = useState<DashboardFilters>(initialFilters);
  const [draftFilters, setDraftFilters] = useState<DashboardFilters>(initialFilters);
  useEffect(() => {
    const next = { startDate: searchParams.get('startDate') ?? defaultPeriod().startDate,
      endDate: searchParams.get('endDate') ?? defaultPeriod().endDate };
    setFilters(next);
    setDraftFilters(next);
  }, [searchParams]);
  const [selectedMetrics, setSelectedMetrics] = useState<MetricKey[]>(['revenue']);
  const toggleMetric = (metric: MetricKey) => setSelectedMetrics((current) =>
    current.includes(metric) ? current.filter((item) => item !== metric) : [...current, metric]);
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
  const prior = previousPeriod(filters);
  const priorDaily = useQuery({ queryKey: ['campaign-daily', id, prior],
    queryFn: () => getDailySeries(prior, id), enabled: Boolean(id) });
  const priorSummary = useQuery({ queryKey: ['campaign-summary', id, prior],
    queryFn: () => getCampaignSummary(id, prior), enabled: Boolean(id) });
  const periods = useQuery({ queryKey: queryKeys.statistics.periods, queryFn: getCampaignPeriods });

  if (!id) return <Navigate to={appRoutes.dashboard} replace />;
  const loading = summary.isLoading || clusters.isLoading || articles.isLoading;
  const error = summary.error ?? clusters.error ?? articles.error;
  const recommendations = [
    ...campaignRecommendations(summary.data ? [summary.data] : []),
    ...clusterRecommendations(id, clusters.data?.isPeriodComplete ? clusters.data.rows : [])
  ];

  return <Stack spacing={2.5}>
    <Breadcrumbs aria-label="Навигация"><Link href={`${appRoutes.dashboard}?startDate=${filters.startDate}&endDate=${filters.endDate}`} underline="hover">Сводка</Link>
      <Typography color="text.secondary">Рекламные кампании</Typography><Typography>{summary.data?.name ?? 'Кампания'}</Typography></Breadcrumbs>
    <PageHeader compact title={summary.data?.name ?? 'Кампания'} description={summary.data ?
      <Stack spacing={0.5}>
        <Stack direction="row" gap={0.75} useFlexGap flexWrap="wrap" alignItems="center">
          {summary.data.wbStatus != null ? <Chip size="small" label={summary.data.wbStatus === 9 ? 'Активна' : summary.data.wbStatus === 11 ? 'Приостановлена' : `Статус WB: ${summary.data.wbStatus}`}
            color={summary.data.wbStatus === 9 ? 'success' : 'default'} sx={{ borderRadius: '8px' }} /> : null}
          {summary.data.goal ? <Chip size="small" variant="outlined" label={`Цель: ${summary.data.goal}`} sx={{ borderRadius: '8px' }} /> : null}
          <Chip size="small" variant="outlined" color="primary" label={`Норма ДРР рекламы ${formatPercent(summary.data.targetDrr, 1)}`}
            sx={{ borderRadius: '8px' }} />
        </Stack>
        <Typography variant="caption" color="text.secondary">Сравнение с {prior.startDate?.slice(5).split('-').reverse().join('.')} – {prior.endDate?.slice(5).split('-').reverse().join('.')} · даты по МСК</Typography>
      </Stack> : undefined}
      actions={<PeriodFilter draftFilters={draftFilters} periods={periods.data ?? []}
        onDraftChange={setDraftFilters} onApply={(selected) => {
          const next = { startDate: selected.startDate || undefined, endDate: selected.endDate || undefined };
          setFilters(next);
          const query = new URLSearchParams();
          if (next.startDate) query.set('startDate', next.startDate);
          if (next.endDate) query.set('endDate', next.endDate);
          setSearchParams(query);
        }} />} />
    {loading ? <LoadingState title="Загружаем кампанию" /> : null}
    {error ? <ErrorState title="Не удалось загрузить кампанию"
      description={error instanceof Error ? error.message : 'Проверьте соединение.'}
      onRetry={() => { void summary.refetch(); void clusters.refetch(); void articles.refetch(); }} /> : null}
    {!loading && !error ? <>
      {summary.data && summary.data.kpi.coverageDays < summary.data.kpi.expectedDays ?
        <Alert severity="warning" variant="outlined" sx={{ py: 0, '& .MuiAlert-message': { py: 0.5 } }}>Загружено {summary.data.kpi.coverageDays} из {summary.data.kpi.expectedDays} дней. Оценки по этому периоду предварительные.</Alert> : null}
      <Stack spacing={0.5}>
      <CampaignKpiGrid campaign={summary.data ?? null} priorCampaign={priorSummary.data ?? null}
        selectedMetrics={selectedMetrics} onSelect={toggleMetric} />
      {!completeKpi(summary.data) || !completeKpi(priorSummary.data) ? <Typography variant="caption" color="text.secondary" sx={{ px: .5 }}>
        Сравнение KPI появится после полной загрузки обоих периодов.
      </Typography> : null}
      </Stack>
      <Card><CardContent>{daily.isError ? <Alert severity="error">Не удалось загрузить дневную динамику.</Alert> :
        <DailyChart days={daily.data ?? []} previousDays={priorDaily.data ?? []} selectedMetrics={selectedMetrics} />}
        {trend.isError ? <Alert severity="error">Не удалось загрузить сравнение расходов.</Alert> : null}
        {trend.data?.status === 'no_baseline' ? <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
          Сравнение расхода: за предыдущие семь дней расход был нулевым, поэтому процент не рассчитывается.
        </Typography> : null}
        {trend.data && !['insufficient', 'no_baseline'].includes(trend.data.status) ? <Typography variant="body2" color={
          trend.data.status === 'increase' ? 'warning.main' : 'text.secondary'} sx={{ mt: 1 }}>
          Расход за {trend.data.endDate}: {formatMoney(trend.data.yesterdaySpend ?? 0)} против среднего {formatMoney(trend.data.baselineDailySpend ?? 0)} за предыдущую неделю
          {' '}({formatPercent(trend.data.changePercent ?? 0, 1)}). Порог: {formatPercent(trend.data.thresholdPercent, 0)}.
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
        {clusters.data?.isWbConnected ? <WbClusterTable rows={clusters.data.rows} jamRows={jam.data?.rows} />
          : <Alert severity="info">Подключите кабинет WB для просмотра кластеров.</Alert>}
      </CardContent></Card>
      {recommendations.length ? <Card><CardContent>
        <Typography variant="h6" fontWeight={800} sx={{ mb: 1 }}>Рекомендации</Typography>
        <RecommendationList startDate={filters.startDate} endDate={filters.endDate} items={recommendations} emptyText="" />
      </CardContent></Card> : <Typography variant="body2" color="text.secondary" sx={{ px: 0.5 }}>
        Рекомендаций пока нет: подтверждённых отклонений по выбранному периоду не найдено или данных недостаточно.
      </Typography>}
      <Card><CardContent>
        <Typography variant="h6" fontWeight={800} sx={{ mb: 2 }}>Поисковые запросы Джема</Typography>
        {jam.isLoading ? <LoadingState title="Загружаем запросы Джема" /> :
          jam.isError ? <Alert severity="error">Не удалось загрузить отчёт Джема.</Alert> :
          jam.data ? <WbJamTable report={jam.data} /> : null}
      </CardContent></Card>
    </> : null}
  </Stack>;
}
