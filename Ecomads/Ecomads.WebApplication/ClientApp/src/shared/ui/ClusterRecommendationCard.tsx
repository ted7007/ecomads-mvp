import { Accordion, AccordionDetails, AccordionSummary, Alert, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import type { RecommendationCard } from '../api/clusterRecommendationApi';
import { saveSnapshotDecision } from '../api/clusterRecommendationApi';
import { formatMoney } from '../lib/formatMoney';
import { moscowToday } from '../../pages/DashboardPage/components/PeriodFilter';
import { RecommendationExposure } from './RecommendationExposure';
import { recommendationAction, recommendationDates, recommendationDetailActionNotes, recommendationDetailCalculation, recommendationDetailReason, recommendationPeriod, recommendationTitle } from '../lib/recommendationPresentation';

const labels: Record<string, string> = { accepted: 'Принял', snoozed: 'Отложено', irrelevant: 'Не актуально' };
export function DecisionButtons({ campaignId, runId, evaluationIds, ruleId, compact = false }: {
  campaignId: string; runId: string; evaluationIds: string[]; ruleId: string;
  compact?: boolean;
}) {
  const [status, setStatus] = useState('');
  const [date, setDate] = useState('');
  const [reason, setReason] = useState('');
  const [reasonType, setReasonType] = useState('other');
  const autoReplenishment = ruleId === 'R02' && status === 'irrelevant' && reasonType === 'budget_auto_replenishment';
  const [comment, setComment] = useState('');
  const cache = useQueryClient();
  const save = useMutation({ mutationFn: () => saveSnapshotDecision(campaignId, runId, evaluationIds, ruleId, status, date || null,
    autoReplenishment ? 'Включено автопополнение' : reason || null, comment || null, autoReplenishment ? 'budget_auto_replenishment' : null),
    onSuccess: async () => { setStatus(''); await Promise.all([
      cache.invalidateQueries({ queryKey: ['wb-cluster-recommendations'] }), cache.invalidateQueries({ queryKey: ['wb-recommendation-summary'] }), cache.invalidateQueries({ queryKey: ['wb-results'] }),
      cache.invalidateQueries({ queryKey: ['wb-campaign-norms', campaignId] }), cache.invalidateQueries({ queryKey: ['wb-campaign-norm-list'] })
    ]); } });
  const invalid = status === 'snoozed' && (!date || date <= moscowToday()) || status === 'irrelevant' && !autoReplenishment && !reason.trim();
  return <>
    <Stack direction="row" gap={1} useFlexGap flexWrap="wrap">{Object.entries(labels).map(([key, label]) =>
      <Button key={key} size="small" variant={compact ? key === 'accepted' ? 'contained' : 'outlined' : 'text'}
        sx={compact ? { flex: { xs: 1, md: 'initial' }, px: 1, minWidth: 0, fontSize: 12, whiteSpace: 'nowrap' } : undefined}
        disabled={!evaluationIds.length} onClick={() => { setStatus(key); setReasonType('other'); save.reset(); }}>{label === 'Отложено' ? 'Отложить' : label}</Button>)}</Stack>
    <Dialog open={Boolean(status)} onClose={() => !save.isPending && setStatus('')} fullWidth maxWidth="sm">
      <DialogTitle>{status === 'snoozed' ? 'Отложить' : labels[status]}{evaluationIds.length > 1 ? ` · выбрано: ${evaluationIds.length}` : ''}</DialogTitle>
      <DialogContent><Stack spacing={2} sx={{ pt: 1 }}>
        <Typography variant="body2">{autoReplenishment ? 'Рекомендации о пополнении бюджета этой кампании будут скрыты. Вернуть их можно в разделе «Нормы».'
          : status === 'accepted' ? 'Отметим рекомендацию как принятую. Изменения в WB вносятся вручную.'
          : status === 'snoozed' ? 'Рекомендация будет отложена до выбранной даты.'
          : 'Сохраним причину, по которой рекомендация не актуальна.'}</Typography>
        {status === 'snoozed' ? <TextField type="date" label="Отложить до" InputLabelProps={{ shrink: true }} value={date} onChange={e => setDate(e.target.value)} required /> : null}
        {status === 'irrelevant' && ruleId === 'R02' ? <TextField select label="Причина" value={reasonType} onChange={e => setReasonType(e.target.value)}>
          <MenuItem value="budget_auto_replenishment">Включено автопополнение</MenuItem><MenuItem value="other">Другая причина</MenuItem>
        </TextField> : null}
        {status === 'irrelevant' && !autoReplenishment ? <TextField label={ruleId === 'R02' ? 'Укажите причину' : 'Причина'} value={reason} onChange={e => setReason(e.target.value)} required inputProps={{ maxLength: 1000 }} /> : null}
        <TextField multiline label="Комментарий (необязательно)" value={comment} onChange={e => setComment(e.target.value)} inputProps={{ maxLength: 2000 }} />
        {save.isError ? <Alert severity="error">Не удалось сохранить решение. Проверьте данные и повторите.</Alert> : null}
      </Stack></DialogContent>
      <DialogActions><Button disabled={save.isPending} onClick={() => setStatus('')}>Отмена</Button><Button disabled={invalid || save.isPending} onClick={() => save.mutate()}>Сохранить</Button></DialogActions>
    </Dialog>
  </>;
}

export function ClusterRecommendationCard({ card, runId, window, quality }: {
  card: RecommendationCard; runId: string | null;
  window: Parameters<typeof recommendationDates>[0];
  quality?: Parameters<typeof recommendationDetailActionNotes>[1];
}) {
  const p = card.presentation;
  const action = recommendationAction(card);
  const notes = recommendationDetailActionNotes(card, quality);
  const calculation = recommendationDetailCalculation(card);
  const details = p?.details;
  const watching = p?.primary.kind === 'watch' && card.subject.scope === 'cluster' && p.control?.excluded !== true &&
    !p.restrictions.some(r => r.code === 'excluded');
  return <Stack spacing={1.5} sx={{ border: '1px solid', borderColor: 'divider', borderRadius: 2, p: 2 }}>
    <RecommendationExposure runId={runId} evaluationId={card.evaluationId} ruleId={card.rule.ruleId} />
    <Stack direction="row" gap={1} useFlexGap flexWrap="wrap" alignItems="center">
      <Typography fontWeight={700}>{recommendationTitle(card)}</Typography>
      {card.preliminary ? <Chip size="small" label="Предварительно" color="warning" variant="outlined" /> : null}
      {card.decision ? <Chip size="small" color={card.decision.status === 'accepted' ? 'success' : card.decision.status === 'snoozed' ? 'info' : 'default'} variant="outlined" label={`${labels[card.decision.status] ?? card.decision.status}${card.decision.snoozeUntil ? ` до ${card.decision.snoozeUntil}` : ''}`} /> : null}
    </Stack>
    <Typography>{[card.subject.clusterName ?? card.subject.nomenclatureName, card.subject.nomenclatureId && `артикул ${card.subject.nomenclatureId}`].filter(Boolean).join(' · ') || 'Вся кампания'}</Typography>
    <Typography variant="caption" color="text.secondary">{recommendationPeriod(card, window)}</Typography>
    <Stack spacing={.5}>
      <Typography component="h3" variant="subtitle2">Почему появился совет</Typography>
      <Typography variant="body2">{recommendationDetailReason(card)}</Typography>
      {details?.kind === 'stock' && card.rule.reasonCode !== 'out_of_stock' && details.fbw != null && details.fbs != null ?
        <Typography variant="caption" color="text.secondary">На складах WB: {details.fbw.toLocaleString('ru-RU')}, у продавца: {details.fbs.toLocaleString('ru-RU')}.</Typography> : null}
    </Stack>
    {notes.length || action || (details?.kind === 'budget' && details.balance != null) ? <Stack spacing={.5}>
      <Typography component="h3" variant="subtitle2">{watching && notes.length ? 'Почему пока наблюдаем' : 'Что делать'}</Typography>
      {notes.map(message => <Typography key={message} variant="body2">{message}</Typography>)}
      {action && !(watching && notes.length) ? <Typography variant="body2">{action}</Typography> : null}
      {details?.kind === 'budget' && details.balance != null ?
        <Typography variant="body2">На рекламном счёте: {formatMoney(details.balance, Number.isInteger(details.balance) ? 0 : 2)}.</Typography> : null}
    </Stack> : null}
    {p?.alternatives.map((alternative, index) => {
      const text = recommendationAction(card, alternative);
      return text ? <Typography key={index} variant="body2">{alternative.kind === p.primary.kind ? 'Дополнительно' : 'Другой вариант'}: {text}</Typography> : null;
    })}
    {calculation ? <Accordion disableGutters elevation={0} slotProps={{ transition: { unmountOnExit: true } }}
      sx={{ borderTop: '1px solid', borderColor: 'divider', '&::before': { display: 'none' } }}>
      <AccordionSummary expandIcon="⌄" sx={{ px: 0 }}>{calculation.title}</AccordionSummary>
      <AccordionDetails sx={{ px: 0 }}>
        {calculation.lines.map(line => <Typography key={line} variant="body2">{line}</Typography>)}
      </AccordionDetails>
    </Accordion> : null}
    {card.decision?.reason ? <Typography variant="body2">Причина: {card.decision.reason}</Typography> : null}
    {card.decision?.comment ? <Typography variant="body2">Комментарий: {card.decision.comment}</Typography> : null}
    {runId && p ? <DecisionButtons campaignId={card.subject.campaignId} runId={runId} evaluationIds={[card.evaluationId]} ruleId={card.rule.ruleId} /> : null}
  </Stack>;
}
