import RefreshIcon from '@mui/icons-material/Refresh';
import UploadIcon from '@mui/icons-material/Upload';
import { Alert, Button, Card, CardContent, Stack, Typography } from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useLocation } from 'react-router-dom';
import { queryKeys } from '../../shared/api/queryKeys';
import type { DashboardFilters, UploadStatisticsRequest } from './dashboardApi';
import { getCampaigns, getLoadedPeriods, uploadDashboardStatistics } from './dashboardApi';
import { CampaignsTable } from './components/CampaignsTable';
import { DashboardKpiGrid } from './components/DashboardKpiGrid';
import { PeriodFilter } from './components/PeriodFilter';
import { UploadStatisticsDialog } from './components/UploadStatisticsDialog';
import { ErrorState } from '../../shared/ui/ErrorState';
import { LoadingState } from '../../shared/ui/LoadingState';
import { PageHeader } from '../../shared/ui/PageHeader';

export function DashboardPage() {
  const location = useLocation();
  const [filters, setFilters] = useState<DashboardFilters>({});
  const [draftFilters, setDraftFilters] = useState<DashboardFilters>({});
  const [uploadOpen, setUploadOpen] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const [uploadSuccess, setUploadSuccess] = useState(false);
  const queryClient = useQueryClient();

  const campaignsQuery = useQuery({
    queryKey: queryKeys.projects.list(filters),
    queryFn: () => getCampaigns(filters)
  });

  const periodsQuery = useQuery({
    queryKey: queryKeys.statistics.periods,
    queryFn: getLoadedPeriods
  });

  const uploadMutation = useMutation({
    mutationFn: (request: UploadStatisticsRequest) => uploadDashboardStatistics(request),
    onSuccess: async () => {
      setUploadOpen(false);
      setUploadError(null);
      setUploadSuccess(true);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['projects'] }),
        queryClient.invalidateQueries({ queryKey: queryKeys.statistics.periods })
      ]);
    },
    onError: (error) => {
      setUploadError(error instanceof Error ? error.message : 'Ошибка загрузки статистики');
    }
  });

  const campaigns = campaignsQuery.data ?? [];
  const demoFeedbackSuccess = (location.state as { demoFeedbackSuccess?: string } | null)?.demoFeedbackSuccess;

  return (
    <Stack spacing={3}>
      <PageHeader
        title="Обзор рекламы"
        actions={
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
            <Button
              color="inherit"
              startIcon={<RefreshIcon />}
              variant="outlined"
              onClick={() => {
                void campaignsQuery.refetch();
                void periodsQuery.refetch();
              }}
              sx={{ color: '#F8FAFC', borderColor: 'rgba(248,250,252,0.4)' }}
            >
              Обновить
            </Button>
            <Button startIcon={<UploadIcon />} variant="contained" onClick={() => setUploadOpen(true)}>
              Загрузить новую статистику
            </Button>
          </Stack>
        }
      />

      {uploadSuccess ? <Alert severity="success">Данные успешно загружены.</Alert> : null}
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
              <Stack spacing={2}>
                <Typography variant="h6" fontWeight={800}>
                  Рекламные кампании
                </Typography>
                <CampaignsTable campaigns={campaigns} />
              </Stack>
            </CardContent>
          </Card>
        </>
      ) : null}

      <UploadStatisticsDialog
        error={uploadError}
        isUploading={uploadMutation.isPending}
        open={uploadOpen}
        onClose={() => {
          setUploadOpen(false);
          setUploadError(null);
        }}
        onSubmit={(request) => uploadMutation.mutateAsync(request)}
      />
    </Stack>
  );
}
