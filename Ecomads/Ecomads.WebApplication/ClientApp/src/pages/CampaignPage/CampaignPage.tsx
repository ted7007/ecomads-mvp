import RefreshIcon from '@mui/icons-material/Refresh';
import UploadIcon from '@mui/icons-material/Upload';
import BoltIcon from '@mui/icons-material/Bolt';
import CloseIcon from '@mui/icons-material/Close';
import { Alert, Box, Button, Card, CardContent, Drawer, IconButton, Stack, Typography } from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { Navigate, useParams } from 'react-router-dom';
import { appRoutes } from '../../app/routes';
import { queryKeys } from '../../shared/api/queryKeys';
import { ErrorState } from '../../shared/ui/ErrorState';
import { LoadingState } from '../../shared/ui/LoadingState';
import { PageHeader } from '../../shared/ui/PageHeader';
import { PeriodFilter } from '../DashboardPage/components/PeriodFilter';
import type { DashboardFilters } from '../DashboardPage/dashboardApi';
import {
  generateCampaignRecommendation,
  getCampaignPeriods,
  getCampaignSummary,
  getKeywordOverlay,
  updateInsightComment,
  updateInsightDecision,
  uploadKeywordStats,
  type InsightDecision,
  type UploadKeywordStatsRequest
} from './campaignApi';
import { normalizeKeywordRow } from './campaignFormatters';
import type { KeywordRecommendationRow } from './campaignSchemas';
import { CampaignKpiGrid } from './components/CampaignKpiGrid';
import { InsightPanel } from './components/InsightPanel';
import { KeywordOverlaySummary } from './components/KeywordOverlaySummary';
import { KeywordTable, type KeywordSort, type KeywordSortField } from './components/KeywordTable';
import { UploadKeywordStatsDialog } from './components/UploadKeywordStatsDialog';

export function CampaignPage() {
  const { campaignId } = useParams();
  const campaignIdValue = campaignId ?? '';
  const queryClient = useQueryClient();
  const [filters, setFilters] = useState<DashboardFilters>({});
  const [draftFilters, setDraftFilters] = useState<DashboardFilters>({});
  const [selectedStatus, setSelectedStatus] = useState('All');
  const [selectedInsightId, setSelectedInsightId] = useState('');
  const [selectedKeywordId, setSelectedKeywordId] = useState('');
  const [sort, setSort] = useState<KeywordSort>({ field: '', direction: 'asc' });
  const [uploadOpen, setUploadOpen] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  const summaryQuery = useQuery({
    enabled: Boolean(campaignIdValue),
    queryKey: ['campaign-summary', campaignIdValue, filters],
    queryFn: () => getCampaignSummary(campaignIdValue, filters)
  });

  const periodsQuery = useQuery({
    queryKey: queryKeys.statistics.periods,
    queryFn: getCampaignPeriods
  });

  const overlayQuery = useQuery({
    enabled: Boolean(campaignIdValue),
    queryKey: queryKeys.recommendations.keywordOverlay(campaignIdValue, filters),
    queryFn: () => getKeywordOverlay(campaignIdValue, filters)
  });

  const generateMutation = useMutation({
    mutationFn: () => generateCampaignRecommendation(campaignIdValue),
    onSuccess: async () => {
      setSuccessMessage('Insights успешно обновлены.');
      await queryClient.invalidateQueries({ queryKey: queryKeys.recommendations.keywordOverlay(campaignIdValue, filters) });
    }
  });

  const decisionMutation = useMutation({
    mutationFn: ({ insightId, decision }: { insightId: string; decision: InsightDecision }) => updateInsightDecision(insightId, decision),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.recommendations.keywordOverlay(campaignIdValue, filters) });
    }
  });

  const commentMutation = useMutation({
    mutationFn: ({ insightId, comment }: { insightId: string; comment: string }) => updateInsightComment(insightId, comment),
    onSuccess: async () => {
      setSuccessMessage('Комментарий сохранён.');
      await queryClient.invalidateQueries({ queryKey: queryKeys.recommendations.keywordOverlay(campaignIdValue, filters) });
    }
  });

  const uploadMutation = useMutation({
    mutationFn: (request: UploadKeywordStatsRequest) => uploadKeywordStats(request),
    onSuccess: async () => {
      setUploadOpen(false);
      setUploadError(null);
      setSuccessMessage('Ключевые слова успешно загружены.');
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.recommendations.keywordOverlay(campaignIdValue, filters) }),
        queryClient.invalidateQueries({ queryKey: ['campaign-summary'] })
      ]);
    },
    onError: (error) => {
      setUploadError(error instanceof Error ? error.message : 'Ошибка загрузки ключевых слов');
    }
  });

  const overlay = overlayQuery.data;
  const rows = useMemo(() => {
    const normalizedRows = (overlay?.keywords ?? []).map(normalizeKeywordRow);
    const filteredRows = selectedStatus === 'All' ? normalizedRows : normalizedRows.filter((row) => row.status === selectedStatus);

    if (!sort.field) {
      return filteredRows;
    }

    return [...filteredRows].sort((left, right) => compareRows(left, right, sort));
  }, [overlay?.keywords, selectedStatus, sort]);

  const selectedInsight = useMemo(
    () => overlay?.insights.find((insight) => insight.insightId === selectedInsightId) ?? null,
    [overlay?.insights, selectedInsightId]
  );
  const selectedKeyword = useMemo(
    () => rows.find((row) => row.keywordId === selectedKeywordId) ?? null,
    [rows, selectedKeywordId]
  );
  const isLoading = summaryQuery.isLoading || overlayQuery.isLoading;
  const isError = summaryQuery.isError || overlayQuery.isError;

  if (!campaignIdValue) {
    return <Navigate to={appRoutes.dashboard} replace />;
  }

  const handleSortChange = (field: KeywordSortField) => {
    setSort((current) => ({
      field,
      direction: current.field === field && current.direction === 'asc' ? 'desc' : 'asc'
    }));
  };

  const handleSelectRow = (row: KeywordRecommendationRow) => {
    setSelectedKeywordId(row.keywordId);
    setSelectedInsightId(row.mainInsightId ?? '');
  };

  const handleCloseDetails = () => {
    setSelectedKeywordId('');
    setSelectedInsightId('');
  };

  return (
    <Stack spacing={2.5}>
      <PageHeader
        title={summaryQuery.data?.name ?? 'Кампания'}
        actions={
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
            <Button
              color="inherit"
              startIcon={<RefreshIcon />}
              variant="outlined"
              onClick={() => {
                void summaryQuery.refetch();
                void overlayQuery.refetch();
                void periodsQuery.refetch();
              }}
              sx={{ color: '#F8FAFC', borderColor: 'rgba(248,250,252,0.4)' }}
            >
              Обновить
            </Button>
            <Button startIcon={<BoltIcon />} variant="contained" onClick={() => generateMutation.mutate()} disabled={generateMutation.isPending}>
              {generateMutation.isPending ? 'Генерируем...' : 'Обновить insights'}
            </Button>
            <Button startIcon={<UploadIcon />} variant="contained" onClick={() => setUploadOpen(true)}>
              Загрузить ключевые слова
            </Button>
          </Stack>
        }
      />

      {successMessage ? <Alert severity="success" onClose={() => setSuccessMessage(null)}>{successMessage}</Alert> : null}
      {generateMutation.isError ? <Alert severity="error">{generateMutation.error instanceof Error ? generateMutation.error.message : 'Ошибка генерации insights'}</Alert> : null}

      <Card sx={{ borderRadius: 2 }}>
        <CardContent>
          <PeriodFilter
            draftFilters={draftFilters}
            periods={periodsQuery.data ?? []}
            onApply={() => {
              setFilters({ startDate: draftFilters.startDate || undefined, endDate: draftFilters.endDate || undefined });
              setSelectedInsightId('');
              setSelectedKeywordId('');
            }}
            onDraftChange={setDraftFilters}
          />
        </CardContent>
      </Card>

      {isLoading ? <LoadingState title="Загружаем кампанию" /> : null}
      {isError ? (
        <ErrorState
          title="Не удалось загрузить кампанию"
          description={
            summaryQuery.error instanceof Error
              ? summaryQuery.error.message
              : overlayQuery.error instanceof Error
                ? overlayQuery.error.message
                : 'Проверьте соединение и авторизацию.'
          }
          onRetry={() => {
            void summaryQuery.refetch();
            void overlayQuery.refetch();
          }}
        />
      ) : null}

      {!isLoading && !isError ? (
        <>
          <CampaignKpiGrid campaign={summaryQuery.data ?? null} />

          <Card sx={{ borderRadius: 2 }}>
            <CardContent sx={{ p: 2, '&:last-child': { pb: 2 } }}>
              <KeywordOverlaySummary overlay={overlay} selectedStatus={selectedStatus} onStatusChange={setSelectedStatus} />
            </CardContent>
          </Card>

          <Card sx={{ overflow: 'hidden', borderRadius: 2 }}>
            <CardContent sx={{ p: 0, '&:last-child': { pb: 0 } }}>
              <Stack spacing={2}>
                <Typography variant="h6" fontWeight={800} sx={{ px: 2, pt: 2 }}>
                  Статистика по ключевым словам
                </Typography>
                <KeywordTable
                  rows={rows}
                  selectedInsightId={selectedInsightId}
                  selectedKeywordId={selectedKeywordId}
                  sort={sort}
                  onSelect={handleSelectRow}
                  onSortChange={handleSortChange}
                />
              </Stack>
            </CardContent>
          </Card>

          <Drawer
            anchor="right"
            hideBackdrop
            open={Boolean(selectedKeyword)}
            onClose={handleCloseDetails}
            PaperProps={{
              sx: {
                width: { xs: '100%', sm: 460, lg: 500 },
                bgcolor: '#F8FAFC',
                boxShadow: '-18px 0 35px -24px rgba(15, 23, 42, 0.55)'
              }
            }}
          >
            <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', px: 2.5, py: 2, borderBottom: '1px solid #CBD5E1' }}>
              <Typography fontWeight={800}>Детали ключевого слова</Typography>
              <IconButton aria-label="Закрыть детали" onClick={handleCloseDetails}>
                <CloseIcon />
              </IconButton>
            </Box>
            <Box sx={{ p: 2.5, overflowY: 'auto' }}>
              <InsightPanel
                insight={selectedInsight}
                keyword={selectedKeyword}
                isUpdating={decisionMutation.isPending || commentMutation.isPending}
                onDecision={(insightId, decision) => decisionMutation.mutate({ insightId, decision })}
                onSaveComment={(insightId, comment) => commentMutation.mutate({ insightId, comment })}
              />
            </Box>
          </Drawer>
        </>
      ) : null}

      <UploadKeywordStatsDialog
        campaignId={campaignIdValue}
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

function compareRows(left: KeywordRecommendationRow, right: KeywordRecommendationRow, sort: KeywordSort): number {
  const direction = sort.direction === 'asc' ? 1 : -1;
  const leftValue = sort.field ? left[sort.field] : '';
  const rightValue = sort.field ? right[sort.field] : '';

  if (typeof leftValue === 'string' || typeof rightValue === 'string') {
    return String(leftValue ?? '').localeCompare(String(rightValue ?? ''), 'ru') * direction;
  }

  return ((Number(leftValue) || 0) - (Number(rightValue) || 0)) * direction;
}
