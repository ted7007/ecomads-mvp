import RefreshIcon from '@mui/icons-material/Refresh';
import { Button, Card, CardContent, FormControl, InputLabel, MenuItem, Select, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { queryKeys } from '../../shared/api/queryKeys';
import { ErrorState } from '../../shared/ui/ErrorState';
import { LoadingState } from '../../shared/ui/LoadingState';
import { PageHeader } from '../../shared/ui/PageHeader';
import { RecommendationChart } from './components/RecommendationChart';
import { RecommendationsTable } from './components/RecommendationsTable';
import { RecommendationStatsCards } from './components/RecommendationStatsCards';
import { RecommendationStatusSummary } from './components/RecommendationStatusSummary';
import { getRecommendationStats, reportPeriods, type ReportPeriod } from './reportApi';

export function ReportPage() {
  const [period, setPeriod] = useState<ReportPeriod>('month');
  const statsQuery = useQuery({
    queryKey: queryKeys.recommendations.stats(period),
    queryFn: () => getRecommendationStats(period)
  });

  const stats = statsQuery.data;

  return (
    <Stack spacing={3}>
      <PageHeader
        title="Отчёт эффективности"
        actions={
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
            <FormControl size="small" sx={{ minWidth: 220, bgcolor: 'background.paper', borderRadius: 2 }}>
              <InputLabel id="report-period-label">Период</InputLabel>
              <Select
                labelId="report-period-label"
                label="Период"
                value={period}
                onChange={(event) => setPeriod(event.target.value as ReportPeriod)}
              >
                {reportPeriods.map((item) => (
                  <MenuItem key={item.value} value={item.value}>
                    {item.label}
                  </MenuItem>
                ))}
              </Select>
            </FormControl>
            <Button
              color="inherit"
              startIcon={<RefreshIcon />}
              variant="outlined"
              onClick={() => void statsQuery.refetch()}
              sx={{ color: '#F8FAFC', borderColor: 'rgba(248,250,252,0.4)' }}
            >
              Обновить данные
            </Button>
          </Stack>
        }
      />

      {statsQuery.isLoading ? <LoadingState title="Загружаем отчёт эффективности" /> : null}

      {statsQuery.isError ? (
        <ErrorState
          title="Не удалось загрузить отчёт"
          description={statsQuery.error instanceof Error ? statsQuery.error.message : 'Проверьте соединение и авторизацию.'}
          onRetry={() => void statsQuery.refetch()}
        />
      ) : null}

      {stats && !statsQuery.isError ? (
        <>
          <RecommendationStatsCards stats={stats} />
          <RecommendationChart monthly={stats.monthly} />
          <RecommendationStatusSummary counts={stats.counts} />
          <Card>
            <CardContent>
              <Stack spacing={2}>
                <Typography variant="h6" fontWeight={800}>
                  Последние рекомендации
                </Typography>
                <RecommendationsTable recommendations={stats.recommendations} />
              </Stack>
            </CardContent>
          </Card>
        </>
      ) : null}
    </Stack>
  );
}
