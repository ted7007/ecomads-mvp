import WarningAmberRoundedIcon from '@mui/icons-material/WarningAmberRounded';
import { Alert, Box, Button, Chip, Stack, Typography } from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { Recommendation } from '../lib/recommendations';
import { getRecommendationDecisions, saveRecommendationDecision } from '../api/recommendationApi';
import type { DecisionStatus } from '../api/recommendationApi';

export function RecommendationList({ items, emptyText, startDate, endDate }: { items: Recommendation[]; emptyText: string;
  startDate?: string; endDate?: string }) {
  const queryClient = useQueryClient();
  const queryKey = ['recommendation-decisions', startDate, endDate];
  const decisions = useQuery({ queryKey, queryFn: () => getRecommendationDecisions(startDate!, endDate!),
    enabled: Boolean(startDate && endDate && items.length) });
  const mutation = useMutation({ mutationFn: ({ key, status }: { key: string; status: DecisionStatus }) =>
    saveRecommendationDecision(key, startDate!, endDate!, status),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey }); } });
  if (!items.length) return <Typography color="text.secondary" sx={{ py: 2 }}>{emptyText}</Typography>;
  return <Stack spacing={1.5}>
    {mutation.isError ? <Alert severity="error">Не удалось сохранить решение. Попробуйте ещё раз.</Alert> : null}
    {items.map((item) => {
      const status = decisions.data?.find((decision) => decision.recommendationKey === item.id)?.status ?? 'open';
      const label = status === 'accepted' ? 'Принято' : status === 'snoozed' ? 'Отложено' :
        status === 'irrelevant' ? 'Не актуально' : 'Проверить';
      return <Box key={item.id} sx={{ p: 2, border: '1px solid rgba(255,149,0,.28)',
      borderRadius: 3, bgcolor: 'rgba(255,149,0,.07)' }}>
      <Stack direction="row" alignItems="center" gap={1} flexWrap="wrap" sx={{ mb: .7 }}>
        <WarningAmberRoundedIcon color="warning" fontSize="small" />
        <Typography fontWeight={750}>{item.title}</Typography>
        <Chip size="small" label={label} sx={{ bgcolor: 'rgba(255,149,0,.14)', color: 'warning.main' }} />
      </Stack>
      <Typography variant="body2">{item.detail}</Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mt: .6 }}>Рекомендуем: {item.action}</Typography>
      {startDate && endDate ? <Stack direction="row" gap={.7} flexWrap="wrap" sx={{ mt: 1.5 }}>
        {status === 'open' ? <>
          <Button size="small" variant="contained" disabled={mutation.isPending} onClick={() => mutation.mutate({ key: item.id, status: 'accepted' })}>Принял</Button>
          <Button size="small" variant="outlined" disabled={mutation.isPending} onClick={() => mutation.mutate({ key: item.id, status: 'snoozed' })}>Отложить</Button>
          <Button size="small" variant="text" disabled={mutation.isPending} onClick={() => mutation.mutate({ key: item.id, status: 'irrelevant' })}>Не актуально</Button>
        </> : <Button size="small" variant="text" disabled={mutation.isPending} onClick={() => mutation.mutate({ key: item.id, status: 'open' })}>Отменить решение</Button>}
      </Stack> : null}
    </Box>;
    })}
  </Stack>;
}
