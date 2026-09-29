import RefreshIcon from '@mui/icons-material/Refresh';
import StorefrontIcon from '@mui/icons-material/Storefront';
import { Alert, Button, Card, CardContent, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { appRoutes } from '../../app/routes';
import { queryKeys } from '../../shared/api/queryKeys';
import type { DashboardFilters } from './dashboardApi';
import { getCampaigns, getLoadedPeriods } from './dashboardApi';
import { CampaignsTable } from './components/CampaignsTable';
import { DashboardKpiGrid } from './components/DashboardKpiGrid';
import { PeriodFilter } from './components/PeriodFilter';
import { ErrorState } from '../../shared/ui/ErrorState';
import { LoadingState } from '../../shared/ui/LoadingState';
import { PageHeader } from '../../shared/ui/PageHeader';

export function DashboardPage() {
  const location = useLocation();
  const navigate = useNavigate();
  const [filters, setFilters] = useState<DashboardFilters>({});
  const [draftFilters, setDraftFilters] = useState<DashboardFilters>({});

  const campaignsQuery = useQuery({
    queryKey: queryKeys.projects.list(filters),
    queryFn: () => getCampaigns(filters)
  });

  const periodsQuery = useQuery({
    queryKey: queryKeys.statistics.periods,
    queryFn: getLoadedPeriods
  });

  const campaigns = campaignsQuery.data ?? [];
  const overTarget = campaigns.filter((campaign) => campaign.kpi.revenue > 0 && campaign.kpi.drr > campaign.targetDrr);
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
            onApply={() => setFilters({ startDate: draftFilters.startDate || undefined, endDate: draftFilters.endDate || undefined })}
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
          <DashboardKpiGrid campaigns={campaigns} />

          <Card>
            <CardContent>
              <Stack spacing={1}>
                <Typography variant="h6" fontWeight={800}>Требуют внимания</Typography>
                {overTarget.length > 0 ? <Typography color="warning.main">
                  ДРР рекламы выше заданной цели у {overTarget.length} {overTarget.length === 1 ? 'кампании' : 'кампаний'} за выбранный период.
                </Typography> : <Typography color="text.secondary">
                  Кампаний с рекламным ДРР выше цели за выбранный период нет. Для кампаний без выручки ДРР не рассчитывается.
                </Typography>}
              </Stack>
            </CardContent>
          </Card>

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
