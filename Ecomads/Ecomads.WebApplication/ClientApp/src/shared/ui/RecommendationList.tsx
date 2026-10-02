import WarningAmberRoundedIcon from '@mui/icons-material/WarningAmberRounded';
import { Alert, Box, Button, Chip, Stack, Typography } from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import type { Recommendation } from '../lib/recommendations';
import { getRecommendationDecisions, saveRecommendationDecision } from '../api/recommendationApi';
import type { DecisionStatus } from '../api/recommendationApi';
import { appRoutes } from '../../app/routes';

export function RecommendationList({ items, emptyText, startDate, endDate, maxVisible }: { items: Recommendation[]; emptyText: string;
  startDate?: string; endDate?: string; maxVisible?: number }) {
  const [showAll, setShowAll] = useState(false);
  const queryClient = useQueryClient();
  const queryKey = ['recommendation-decisions', startDate, endDate];
  const decisions = useQuery({ queryKey, queryFn: () => getRecommendationDecisions(startDate!, endDate!),
    enabled: Boolean(startDate && endDate && items.length) });
  const mutation = useMutation({ mutationFn: ({ key, status }: { key: string; status: DecisionStatus }) =>
    saveRecommendationDecision(key, startDate!, endDate!, status),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey }); } });
  if (!items.length) return <Typography color="text.secondary" sx={{ py: .5 }}>{emptyText}</Typography>;
  return <Stack spacing={1.5}>
    {mutation.isError ? <Alert severity="error">Не удалось сохранить решение. Попробуйте ещё раз.</Alert> : null}
    <Box sx={{ display: 'grid', gridTemplateColumns: maxVisible ? { xs: 'minmax(0, 1fr)', md: 'repeat(2, minmax(0, 1fr))', lg: 'repeat(3, minmax(0, 1fr))' } : { xs: 'minmax(0, 1fr)', lg: 'repeat(2, minmax(0, 1fr))' }, gap: 1.5, minWidth: 0 }}>
    {(maxVisible && !showAll ? items.slice(0, maxVisible) : items).map((item) => {
      const status = decisions.data?.find((decision) => decision.recommendationKey === item.id)?.status ?? 'open';
      const label = status === 'accepted' ? 'Принято' : status === 'snoozed' ? 'Отложено' :
        status === 'irrelevant' ? 'Не актуально' : 'Проверить';
      return <Box key={item.id} sx={{ p: 1.5, minWidth: 0, overflow: 'hidden', border: '1px solid rgba(255,149,0,.28)',
      borderRadius: '18px', bgcolor: 'rgba(255,149,0,.07)' }}>
      <Stack direction="row" alignItems="center" gap={1} flexWrap="wrap" sx={{ mb: .7 }}>
        <WarningAmberRoundedIcon color="warning" fontSize="small" />
        <Typography fontWeight={750} noWrap title={item.title} sx={{ minWidth: 0, flex: 1 }}>{item.title}</Typography>
        <Chip size="small" label={label} sx={{ bgcolor: 'rgba(255,149,0,.14)', color: 'warning.main' }} />
      </Stack>
      <Typography variant="body2" sx={{ fontSize: 13, lineHeight: 1.35 }}>{item.detail}</Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mt: .5, fontSize: 13, lineHeight: 1.35 }}>Рекомендуем: {item.action}</Typography>
      {item.id.startsWith('drr:') || item.id.startsWith('ctr:') ? <Button size="small" component={Link} sx={{ px: 0, minWidth: 0 }} to={`${appRoutes.campaignPath(item.id.slice(4))}?startDate=${startDate}&endDate=${endDate}`}>Открыть</Button> : null}
      {startDate && endDate ? <Stack direction="row" gap={.5} flexWrap="wrap" sx={{ mt: 1 }}>
        {status === 'open' ? <>
          <Button size="small" variant="contained" disabled={mutation.isPending} onClick={() => mutation.mutate({ key: item.id, status: 'accepted' })}>Принял</Button>
          <Button size="small" variant="outlined" disabled={mutation.isPending} onClick={() => mutation.mutate({ key: item.id, status: 'snoozed' })}>Отложить</Button>
          <Button size="small" variant="text" disabled={mutation.isPending} onClick={() => mutation.mutate({ key: item.id, status: 'irrelevant' })}>Не актуально</Button>
        </> : <Button size="small" variant="text" disabled={mutation.isPending} onClick={() => mutation.mutate({ key: item.id, status: 'open' })}>Отменить решение</Button>}
      </Stack> : null}
    </Box>;
    })}
    </Box>
    {maxVisible && items.length > maxVisible ? <Button size="small" onClick={() => setShowAll(!showAll)} sx={{ alignSelf: 'flex-start' }}>{showAll ? 'Скрыть' : `Показать ещё ${items.length - maxVisible}`}</Button> : null}
  </Stack>;
}
