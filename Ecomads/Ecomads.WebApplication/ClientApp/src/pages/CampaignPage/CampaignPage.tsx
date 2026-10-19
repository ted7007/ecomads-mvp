import { Accordion, AccordionDetails, AccordionSummary, Alert, Breadcrumbs, Card, CardContent, Chip, Link, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { Navigate, useParams, useSearchParams } from 'react-router-dom';
import { appRoutes } from '../../app/routes';
import { CampaignNormDialog } from './components/CampaignNormDialog';
import { EmptyState } from '../../shared/ui/EmptyState';
import { ErrorState } from '../../shared/ui/ErrorState';
import { LoadingState } from '../../shared/ui/LoadingState';
import { PageHeader } from '../../shared/ui/PageHeader';
import { formatMoney } from '../../shared/lib/formatMoney';
import { formatPercent } from '../../shared/lib/formatPercent';
import { PeriodFilter, defaultPeriod, moscowToday } from '../DashboardPage/components/PeriodFilter';
import type { DashboardFilters } from '../DashboardPage/dashboardApi';
import { getCampaignSummary, getNomenclatureStatistics, getWbClusters, getWbJam, getWbSpendTrend } from './campaignApi';
import { CampaignKpiGrid } from './components/CampaignKpiGrid';
import { NomenclatureTable } from './components/NomenclatureTable';
import { WbClusterTable } from './components/WbClusterTable';
import { WbJamTable } from './components/WbJamTable';
import { getDailySeries } from '../DashboardPage/dashboardApi';
import { DailyChart } from '../../shared/ui/DailyChart';
import type { MetricKey } from '../../shared/ui/DailyChart';
import { previousPeriod } from '../../shared/lib/previousPeriod';
import { WbDataFreshness } from '../../shared/ui/WbDataFreshness';
import { getClusterRecommendations } from '../../shared/api/clusterRecommendationApi';
import { ClusterRecommendations } from './components/ClusterRecommendations';

export function CampaignPage() {
  const { campaignId } = useParams();
  return campaignId ? <CampaignContent key={campaignId} id={campaignId} /> : <Navigate to={appRoutes.dashboard} replace />;
}

function CampaignContent({ id }: { id: string }) {
  const [normOpen, setNormOpen] = useState(false);
  const [searchParams, setSearchParams] = useSearchParams();
  const initialFilters = (): DashboardFilters => ({
    startDate: searchParams.get('startDate') ?? defaultPeriod().startDate,
    endDate: searchParams.get('endDate') ?? defaultPeriod().endDate
  });
  const [filters, setFilters] = useState<DashboardFilters>(initialFilters);
  const [draftFilters, setDraftFilters] = useState<DashboardFilters>(initialFilters);
  useEffect(() => {
    const next = {
      startDate: searchParams.get('startDate') ?? defaultPeriod().startDate,
      endDate: searchParams.get('endDate') ?? defaultPeriod().endDate
    };
    setFilters(next);
    setDraftFilters(next);
  }, [searchParams]);
  const [selectedMetrics, setSelectedMetrics] = useState<MetricKey[]>(['spend', 'drr']);
  const [jamPeriod, setJamPeriod] = useState<DashboardFilters | null>(null);
  const toggleMetric = (metric: MetricKey) => setSelectedMetrics((current) =>
    current.includes(metric) ? current.filter((item) => item !== metric) : [...current, metric]);
  const effectiveJamPeriod = jamPeriod ?? filters;
  const summary = useQuery({
    queryKey: ['campaign-summary', id, filters],
    queryFn: () => getCampaignSummary(id, filters), enabled: Boolean(id)
  });
  const clusters = useQuery({
    queryKey: ['wb-clusters', id, filters],
    queryFn: () => getWbClusters(id, filters), enabled: Boolean(id)
  });
  const clusterReport = useQuery({
    queryKey: ['wb-cluster-recommendations', id],
    queryFn: () => getClusterRecommendations(id), enabled: Boolean(id)
  });
  const jam = useQuery({
    queryKey: ['wb-jam', id, effectiveJamPeriod],
    queryFn: () => getWbJam(id, effectiveJamPeriod), enabled: Boolean(id)
  });
  const articles = useQuery({
    queryKey: ['campaign-nomenclatures', id, filters],
    queryFn: () => getNomenclatureStatistics(id, filters), enabled: Boolean(id)
  });
  const trend = useQuery({
    queryKey: ['wb-spend-trend', id, filters.endDate],
    queryFn: () => getWbSpendTrend(id, filters.endDate), enabled: Boolean(id) && filters.endDate !== moscowToday()
  });
  const daily = useQuery({
    queryKey: ['campaign-daily', id, filters],
    queryFn: () => getDailySeries(filters, id), enabled: Boolean(id)
  });
  const includeToday = filters.endDate === moscowToday();
  const prior = previousPeriod(filters);
  const priorDaily = useQuery({
    queryKey: ['campaign-daily', id, prior],
    queryFn: () => getDailySeries(prior, id), enabled: Boolean(id) && !includeToday
  });
  const priorSummary = useQuery({
    queryKey: ['campaign-summary', id, prior],
    queryFn: () => getCampaignSummary(id, prior), enabled: Boolean(id) && !includeToday
  });


  const resetKey = `${id}:${filters.startDate}:${filters.endDate}`;
  if (summary.isPending) return <LoadingState title="Загружаем кампанию" />;
  if (summary.isError && !summary.data) return <ErrorState title="Не удалось загрузить кампанию" onRetry={() => void summary.refetch()} />;
  if (!summary.data) return <EmptyState title="Кампания не найдена" />;

  return <Stack spacing={2.5}>
    <Breadcrumbs aria-label="Навигация"><Link href={`${appRoutes.dashboard}?startDate=${filters.startDate}&endDate=${filters.endDate}`} underline="hover">Сводка</Link>
      <Link href={`${appRoutes.dashboard}?startDate=${filters.startDate}&endDate=${filters.endDate}#campaigns`}>Рекламные кампании</Link><Typography>{summary.data?.name ?? 'Кампания'}</Typography></Breadcrumbs>
    <PageHeader compact title={summary.data?.name ?? 'Кампания'} description={summary.data ?
      <Stack spacing={0.5}>
        <Stack direction="row" gap={0.75} useFlexGap flexWrap="wrap" alignItems="center">
          {summary.data.wbStatus != null ? <Chip size="small" label={summary.data.wbStatus === 9 ? 'Активна' : summary.data.wbStatus === 11 ? 'Приостановлена' : `Статус WB: ${summary.data.wbStatus}`}
            color={summary.data.wbStatus === 9 ? 'success' : 'default'} sx={{ borderRadius: '8px' }} /> : null}
          {summary.data.goal ? <Chip size="small" variant="outlined" label={`Цель: ${summary.data.goal}`} sx={{ borderRadius: '8px' }} /> : null}
          <Chip size="small" variant="outlined" color="primary" label={`Норма ДРР рекламы ${formatPercent(summary.data.targetDrr, 1)} · изменить`}
            onClick={() => setNormOpen(true)} sx={{ borderRadius: '8px' }} />
        </Stack>
        <Typography variant="caption" color="text.secondary">Сравнение с {prior.startDate?.slice(5).split('-').reverse().join('.')} – {prior.endDate?.slice(5).split('-').reverse().join('.')} · даты по МСК</Typography>
      </Stack> : undefined}
      actions={<PeriodFilter compact draftFilters={draftFilters} periods={[]}
        onDraftChange={setDraftFilters} onApply={(selected) => {
          const next = { startDate: selected.startDate || undefined, endDate: selected.endDate || undefined };
          setFilters(next);
          const query = new URLSearchParams();
          if (next.startDate) query.set('startDate', next.startDate);
          if (next.endDate) query.set('endDate', next.endDate);
          setSearchParams(query);
        }} />} />
    <WbDataFreshness startDate={filters.startDate!} endDate={filters.endDate!} campaignId={id} />
    {summary.isError ? <ErrorState title="Не удалось обновить показатели кампании" onRetry={() => void summary.refetch()} /> : null}
    <CampaignNormDialog key={String(normOpen)} id={id} open={normOpen} onClose={() => setNormOpen(false)} />
    <CampaignKpiGrid campaign={summary.data ?? null} priorCampaign={includeToday ? null : priorSummary.data ?? null}
      selectedMetrics={selectedMetrics} onSelect={toggleMetric} />
    <Card><CardContent>{daily.isError && !daily.data ? <ErrorState title="Не удалось загрузить дневную динамику" onRetry={() => void daily.refetch()} /> : daily.isPending ? <LoadingState title="Загружаем динамику" /> :
      <DailyChart days={daily.data ?? []} previousDays={includeToday ? [] : priorDaily.data ?? []} selectedMetrics={selectedMetrics} />}
      <Accordion disableGutters elevation={0}><AccordionSummary expandIcon="⌄"><Typography variant="caption">О показателях и сравнении</Typography></AccordionSummary><AccordionDetails>
        <Typography variant="body2">Загружено {summary.data.kpi.coverageDays} из {summary.data.kpi.expectedDays} дней. Нормы подсвечиваются после полной загрузки. Сравнение доступно для двух полных периодов.</Typography>
        {trend.isError ? <ErrorState title="Не удалось загрузить сравнение расходов" onRetry={() => void trend.refetch()} /> : trend.data ? <Typography variant="body2">Расход: {formatMoney(trend.data.yesterdaySpend)} · средний за неделю: {formatMoney(trend.data.baselineDailySpend)} · изменение: {formatPercent(trend.data.changePercent, 1)}.</Typography> : null}
      </AccordionDetails></Accordion>
    </CardContent></Card>
    <Card><CardContent>
      <Typography component="h2" variant="h6" fontWeight={700} sx={{ mb: 2 }}>Товары в кампании</Typography>
      {articles.isError && articles.data ? <ErrorState title="Не удалось обновить товары" onRetry={() => void articles.refetch()} /> : null}
      {articles.isPending ? <LoadingState title="Загружаем товары" /> : articles.isError && !articles.data ? <ErrorState title="Не удалось загрузить товары" onRetry={() => void articles.refetch()} /> : <NomenclatureTable rows={articles.data ?? []} resetKey={resetKey} />}
    </CardContent></Card>
    <Card><CardContent>
      <Typography component="h2" variant="h6" fontWeight={700} sx={{ mb: 2 }}>Поисковые кластеры</Typography>
      {clusters.isError && clusters.data ? <ErrorState title="Не удалось обновить кластеры" onRetry={() => void clusters.refetch()} /> : null}
      {clusters.isPending ? <LoadingState title="Загружаем кластеры" /> : clusters.isError && !clusters.data ? <ErrorState title="Не удалось загрузить кластеры" onRetry={() => void clusters.refetch()} /> : clusters.data?.isWbConnected ? <>
        <WbClusterTable rows={clusters.data.rows} report={clusterReport.data}
          assessmentLoading={clusterReport.isLoading} assessmentError={clusterReport.isError}
          resetKey={resetKey} jamRows={jam.data &&
            jam.data.startDate === filters.startDate && jam.data.endDate === filters.endDate ? jam.data.rows : []} />
        {jam.data?.rows.length && (jam.data.startDate !== filters.startDate || jam.data.endDate !== filters.endDate) ?
          <Typography variant="caption" color="text.secondary">Джем загружен за другой период; его показатели показаны ниже отдельно.</Typography> : null}
      </>
        : <Alert severity="info">Подключите кабинет WB для просмотра кластеров.</Alert>}
    </CardContent></Card>
    {clusterReport.isError && clusterReport.data ? <ErrorState title="Не удалось обновить рекомендации" onRetry={() => void clusterReport.refetch()} /> : null}
    {clusterReport.isError && !clusterReport.data ? <Alert severity="error">Не удалось рассчитать рекомендации за 14 дней.
      <Link component="button" onClick={() => void clusterReport.refetch()}>Повторить</Link></Alert> :
      clusterReport.isLoading ? <LoadingState title="Проверяем рекомендации за 14 дней" /> :
        clusterReport.data?.enabled ? <ClusterRecommendations key={clusterReport.data.runId ?? id} campaignId={id} report={clusterReport.data} /> : <Typography variant="body2" color="text.secondary">Рекомендации отключены или кабинет не подключён.</Typography>}

    <Card><CardContent>
      <Typography component="h2" variant="h6" fontWeight={700} sx={{ mb: 2 }}>Запросы с заказами без совпадения с загруженными кластерами</Typography>
      {jam.isError && jam.data ? <ErrorState title="Не удалось обновить отчёт Джема" onRetry={() => void jam.refetch()} /> : null}
      {jam.isLoading ? <LoadingState title="Загружаем запросы Джема" /> :
        jam.isError && !jam.data ? <ErrorState title="Не удалось загрузить отчёт Джема" onRetry={() => void jam.refetch()} /> :
          jam.data ? <WbJamTable report={jam.data} onSelectPeriod={(startDate, endDate) =>
            setJamPeriod({ startDate, endDate })} /> : null}
    </CardContent></Card>
  </Stack>;
}
