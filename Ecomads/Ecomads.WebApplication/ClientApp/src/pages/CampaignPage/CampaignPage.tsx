import RefreshIcon from '@mui/icons-material/Refresh';
import { Alert, Button, Card, CardContent, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Navigate, useParams, useSearchParams } from 'react-router-dom';
import { appRoutes } from '../../app/routes';
import { queryKeys } from '../../shared/api/queryKeys';
import { ErrorState } from '../../shared/ui/ErrorState';
import { LoadingState } from '../../shared/ui/LoadingState';
import { PageHeader } from '../../shared/ui/PageHeader';
import { PeriodFilter } from '../DashboardPage/components/PeriodFilter';
import type { DashboardFilters } from '../DashboardPage/dashboardApi';
import { getCampaignPeriods, getCampaignSummary, getNomenclatureStatistics, getWbClusters } from './campaignApi';
import { CampaignKpiGrid } from './components/CampaignKpiGrid';
import { NomenclatureTable } from './components/NomenclatureTable';
import { WbClusterTable } from './components/WbClusterTable';

export function CampaignPage() {
  const { campaignId } = useParams();
  const [searchParams, setSearchParams] = useSearchParams();
  const initialFilters = (): DashboardFilters => ({
    startDate: searchParams.get('startDate') ?? undefined,
    endDate: searchParams.get('endDate') ?? undefined
  });
  const [filters, setFilters] = useState<DashboardFilters>(initialFilters);
  const [draftFilters, setDraftFilters] = useState<DashboardFilters>(initialFilters);
  const id = campaignId ?? '';
  const summary = useQuery({ queryKey: ['campaign-summary', id, filters],
    queryFn: () => getCampaignSummary(id, filters), enabled: Boolean(id) });
  const clusters = useQuery({ queryKey: ['wb-clusters', id, filters],
    queryFn: () => getWbClusters(id, filters), enabled: Boolean(id) });
  const articles = useQuery({ queryKey: ['campaign-nomenclatures', id, filters],
    queryFn: () => getNomenclatureStatistics(id, filters), enabled: Boolean(id) });
  const periods = useQuery({ queryKey: queryKeys.statistics.periods, queryFn: getCampaignPeriods });

  if (!id) return <Navigate to={appRoutes.dashboard} replace />;
  const loading = summary.isLoading || clusters.isLoading || articles.isLoading;
  const error = summary.error ?? clusters.error ?? articles.error;

  return <Stack spacing={3}>
    <PageHeader title={summary.data?.name ?? 'Кампания'} actions={<Button startIcon={<RefreshIcon />}
      variant="outlined" onClick={() => { void summary.refetch(); void clusters.refetch(); void articles.refetch(); }}>
      Обновить экран
    </Button>} />
    <Card><CardContent><PeriodFilter draftFilters={draftFilters} periods={periods.data ?? []}
      onDraftChange={setDraftFilters} onApply={() => {
        const next = { startDate: draftFilters.startDate || undefined, endDate: draftFilters.endDate || undefined };
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
      <CampaignKpiGrid campaign={summary.data ?? null} />
      <Card><CardContent>
        <Typography variant="h6" fontWeight={800} sx={{ mb: 2 }}>Товары в кампании</Typography>
        <NomenclatureTable rows={articles.data ?? []} />
      </CardContent></Card>
      <Card><CardContent>
        <Typography variant="h6" fontWeight={800} sx={{ mb: 2 }}>Поисковые кластеры</Typography>
        {clusters.data?.isWbConnected ? <WbClusterTable rows={clusters.data.rows} />
          : <Alert severity="info">Подключите кабинет WB для просмотра кластеров.</Alert>}
      </CardContent></Card>
    </> : null}
  </Stack>;
}
