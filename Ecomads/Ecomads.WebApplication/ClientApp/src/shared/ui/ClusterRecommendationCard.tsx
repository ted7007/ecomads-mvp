import { Alert, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField, Tooltip, Typography } from '@mui/material';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import type { RecommendationCard } from '../api/clusterRecommendationApi';
import { saveSnapshotDecision } from '../api/clusterRecommendationApi';
import { formatMoney } from '../lib/formatMoney';
import { moscowToday } from '../../pages/DashboardPage/components/PeriodFilter';

const labels: Record<string, string> = { accepted: 'Принял', snoozed: 'Отложено', irrelevant: 'Не актуально' };
export function DecisionButtons({ campaignId, runId, evaluationIds, ruleId }: {
  campaignId: string; runId: string; evaluationIds: string[]; ruleId: string;
}) {
  const [status, setStatus] = useState('');
  const [date, setDate] = useState('');
  const [reason, setReason] = useState('');
  const [comment, setComment] = useState('');
  const cache = useQueryClient();
  const save = useMutation({ mutationFn: () => saveSnapshotDecision(campaignId, runId, evaluationIds, ruleId, status, date || null, reason || null, comment || null),
    onSuccess: async () => { setStatus(''); await Promise.all([
      cache.invalidateQueries({ queryKey: ['wb-cluster-recommendations'] }), cache.invalidateQueries({ queryKey: ['wb-recommendation-summary'] })
    ]); } });
  const invalid = status === 'snoozed' && (!date || date <= moscowToday()) || status === 'irrelevant' && !reason.trim();
  return <>
    <Stack direction="row" gap={1} useFlexGap flexWrap="wrap">{Object.entries(labels).map(([key, label]) =>
      <Button key={key} size="small" disabled={!evaluationIds.length} onClick={() => { setStatus(key); save.reset(); }}>{label === 'Отложено' ? 'Отложить' : label}</Button>)}</Stack>
    <Dialog open={Boolean(status)} onClose={() => !save.isPending && setStatus('')} fullWidth maxWidth="sm">
      <DialogTitle>{labels[status]} · выбрано: {evaluationIds.length}</DialogTitle>
      <DialogContent><Stack spacing={2} sx={{ pt: 1 }}>
        <Typography variant="body2">Решение сохранится для этих цифр и действия. Изменения в рекламе WB выполняются вручную.</Typography>
        {status === 'snoozed' ? <TextField type="date" label="Отложить до" InputLabelProps={{ shrink: true }} value={date} onChange={e => setDate(e.target.value)} required /> : null}
        {status === 'irrelevant' ? <TextField label="Причина" value={reason} onChange={e => setReason(e.target.value)} required inputProps={{ maxLength: 1000 }} /> : null}
        <TextField multiline label="Комментарий (необязательно)" value={comment} onChange={e => setComment(e.target.value)} inputProps={{ maxLength: 2000 }} />
        {save.isError ? <Alert severity="error">Не удалось сохранить решение. Проверьте данные и повторите.</Alert> : null}
      </Stack></DialogContent>
      <DialogActions><Button disabled={save.isPending} onClick={() => setStatus('')}>Отмена</Button><Button disabled={invalid || save.isPending} onClick={() => save.mutate()}>Сохранить</Button></DialogActions>
    </Dialog>
  </>;
}

export function ClusterRecommendationCard({ card, runId }: { card: RecommendationCard; runId: string | null }) {
  const p = card.presentation;
  return <Stack spacing={.75} sx={{ border: '1px solid', borderColor: 'divider', borderRadius: 2, p: 2 }}>
    <Stack direction="row" gap={1} useFlexGap flexWrap="wrap" alignItems="center">
      <Typography fontWeight={700}>{card.rule.ruleId === 'R03' ? 'Расход без заказанных единиц' : 'ДРР кластера выше нормы'}</Typography>
      {card.preliminary ? <Chip size="small" label="Предварительно" color="warning" variant="outlined" /> : null}
      {p?.isKey ? <Chip size="small" label={`Ключевой · ${((p.keyShare ?? 0) * 100).toFixed(1)}%`} /> : null}
      {card.decision ? <Chip size="small" color="success" variant="outlined" label={`${labels[card.decision.status] ?? card.decision.status}${card.decision.snoozeUntil ? ` до ${card.decision.snoozeUntil}` : ''}`} /> : null}
    </Stack>
    <Typography>{card.subject.clusterName} · артикул {card.subject.nomenclatureId}</Typography>
    <Typography variant="body2">Расход {formatMoney(card.facts.spend)} · заказанные единицы {card.facts.orderedProducts ?? '—'} · норма ДРР {(card.norm.value * 100).toFixed(2)}%.</Typography>
    <Typography variant="body2">{card.rule.ruleId === 'R03' ? `Порог расхода ${formatMoney(card.rule.threshold ?? 0)}` : `Оценочный ДРР ${((card.economy.drr ?? 0) * 100).toFixed(2)}% · сценарный диапазон ${((card.economy.drrMin ?? 0) * 100).toFixed(2)} — ${((card.economy.drrMax ?? 0) * 100).toFixed(2)}% · с одной дополнительной единицей ${((card.economy.drrPlusOne ?? 0) * 100).toFixed(2)}%`}</Typography>
    <Typography fontWeight={700}>{p?.primary.label ?? card.rule.explanation}</Typography>
    {p?.alternatives.map(a => <Typography key={a.kind} variant="body2">или {a.label.toLocaleLowerCase('ru-RU')}</Typography>)}
    {p?.warning ? <Typography variant="body2">{p.warning}</Typography> : null}
    {p?.restrictions.map(r => <Typography key={r.code} variant="body2" color="text.secondary">{r.label}</Typography>)}
    {p?.diagnostics.map(d => <Tooltip key={d.code} title={d.basis}><Typography variant="body2">{d.label}</Typography></Tooltip>)}
    {p ? <Typography variant="body2">Расход кластера ≈ {formatMoney(p.weeklySpend)} в неделю</Typography> : null}
    {p?.control?.receivedAtUtc ? <Typography variant="caption" color="text.secondary">Настройки WB получены {new Date(p.control.receivedAtUtc).toLocaleString('ru-RU', { timeZone: 'Europe/Moscow' })} МСК.</Typography> : null}
    <Typography variant="caption" color="text.secondary">Сумма заказов оценена по средней цене единицы товара за выбранный период. Данные WB могут уточняться, в том числе после D−2.</Typography>
    {card.decision?.reason ? <Typography variant="body2">Причина: {card.decision.reason}</Typography> : null}
    {card.decision?.comment ? <Typography variant="body2">Комментарий: {card.decision.comment}</Typography> : null}
    {runId && p ? <DecisionButtons campaignId={card.subject.campaignId} runId={runId} evaluationIds={[card.evaluationId]} ruleId={card.rule.ruleId} /> : null}
  </Stack>;
}
