import { Alert, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField, Tooltip, Typography } from '@mui/material';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import type { RecommendationCard } from '../api/clusterRecommendationApi';
import { saveSnapshotDecision } from '../api/clusterRecommendationApi';
import { formatMoney } from '../lib/formatMoney';
import { moscowToday } from '../../pages/DashboardPage/components/PeriodFilter';

const labels: Record<string, string> = { accepted: 'Принял', snoozed: 'Отложено', irrelevant: 'Не актуально' };
export function DecisionButtons({ campaignId, runId, evaluationIds, ruleId, compact = false }: {
  campaignId: string; runId: string; evaluationIds: string[]; ruleId: string;
  compact?: boolean;
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
      <Button key={key} size="small" variant={compact ? key === 'accepted' ? 'contained' : 'outlined' : 'text'}
        sx={compact ? { flex: { xs: 1, md: 'initial' }, px: 1, minWidth: 0, fontSize: 12, whiteSpace: 'nowrap' } : undefined}
        disabled={!evaluationIds.length} onClick={() => { setStatus(key); save.reset(); }}>{label === 'Отложено' ? 'Отложить' : label}</Button>)}</Stack>
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

export function ClusterRecommendationCard({ card, runId, showDecisionButtons = true }: { card: RecommendationCard; runId: string | null; showDecisionButtons?: boolean }) {
  const p = card.presentation;
  const details = p?.details;
  const titles: Record<string, string> = { R01: 'Реклама и остаток товара', R02: 'Бюджета кампании осталось меньше дня', R03: 'Расход без заказанных единиц', R06: 'ДРР кластера выше нормы', R07: 'ДРР товара при единой ставке выше нормы' };
  const sources: Record<string, string> = { stocks_fbw: 'Остатки FBW/FBO', stocks_fbs: 'Остатки FBS', demand: 'Заказы по всем каналам', advertising: 'Состав кампании и ставки', budgets: 'Бюджет кампании', balance: 'Общий баланс' };
  const number = (v: number | null | undefined) => v == null ? '—' : v.toLocaleString('ru-RU', { maximumFractionDigits: 2 });
  return <Stack spacing={.75} sx={{ border: '1px solid', borderColor: 'divider', borderRadius: 2, p: 2 }}>
    <Stack direction="row" gap={1} useFlexGap flexWrap="wrap" alignItems="center">
      <Typography fontWeight={700}>{titles[card.rule.ruleId] ?? card.rule.explanation}</Typography>
      {card.preliminary ? <Chip size="small" label="Предварительно" color="warning" variant="outlined" /> : null}
      {p?.isKey ? <Chip size="small" label={`Ключевой · ${((p.keyShare ?? 0) * 100).toFixed(1)}%`} /> : null}
      {card.decision ? <Chip size="small" color={card.decision.status === 'accepted' ? 'success' : card.decision.status === 'snoozed' ? 'info' : 'default'} variant="outlined" label={`${labels[card.decision.status] ?? card.decision.status}${card.decision.snoozeUntil ? ` до ${card.decision.snoozeUntil}` : ''}`} /> : null}
    </Stack>
    <Typography>{card.subject.scope === 'campaign' ? 'Вся кампания' : `${card.subject.clusterName ? `${card.subject.clusterName} · ` : ''}артикул ${card.subject.nomenclatureId}`}</Typography>
    {details ? <>
      <Typography variant="body2">Данные за {details.window.startDate} — {details.window.endDate}, МСК.</Typography>
      {details.kind === 'stock' ? <>
        <Typography variant="body2">Доступно FBW/FBO: {number(details.fbw)} · FBS: {number(details.fbs)} · запаса на {number(details.stockDays)} дн.</Typography>
        <Typography variant="body2">Заказанные единицы по всем каналам за 14 дней: в среднем {number(details.dailyUnits)} в день. Рекламный расход за последние 3 полных дня: в среднем {formatMoney(details.dailySpend)} в день.</Typography>
        {details.blockGrowth ? <Typography variant="body2">Усиление рекламы ограничено остатком.</Typography> : null}
      </> : details.kind === 'budget' ? <>
        <Typography variant="body2">Бюджет {formatMoney(details.budget)} · хватит на {number(details.budgetDays)} дн. · пополнение {formatMoney(details.topUp)}.</Typography>
        <Typography variant="body2">В среднем в день: расход {formatMoney(details.dailySpend)}, заказы {number(details.dailyUnits)}. ДРР {number(card.economy.drr == null ? null : card.economy.drr * 100)}% при норме {number(card.norm.value * 100)}%.</Typography>
        <Typography variant="body2">Общий баланс счёта: {details.balance == null ? 'не подтверждён' : formatMoney(details.balance)}.</Typography>
      </> : <>
        <Typography variant="body2">Расход {formatMoney(card.facts.spend)} · сумма рекламных заказов {formatMoney(card.economy.estimatedOrderSum)} · заказы O: {number(card.facts.orders)}.</Typography>
        <Typography variant="body2">Фактический ДРР {number(card.economy.drr == null ? null : card.economy.drr * 100)}% · норма {number(card.norm.value * 100)}%. Затронутые товары: {details.affectedArticles?.join(', ')}.</Typography>
      </>}
      {details.sources.map(s => <Typography key={s.source} variant="caption" color="text.secondary">{sources[s.source] ?? s.source}: {s.state === 'ready' ? 'получено' : s.state === 'collecting' ? 'загрузка не завершена' : 'данные недоступны'}{s.receivedAtUtc ? ` ${new Date(s.receivedAtUtc).toLocaleString('ru-RU', { timeZone: 'Europe/Moscow' })} МСК` : ''}{s.errorCode ? ` (${s.errorCode})` : ''}.</Typography>)}
    </> : <>
    <Typography variant="body2">Расход {formatMoney(card.facts.spend)} · заказанные единицы {card.facts.orderedProducts ?? '—'} · норма ДРР {(card.norm.value * 100).toFixed(2)}%.</Typography>
    <Typography variant="body2">{card.rule.ruleId === 'R03' ? `Порог расхода ${formatMoney(card.rule.threshold ?? 0)}` : `Оценочный ДРР ${number(card.economy.drr == null ? null : card.economy.drr * 100)}% · сценарный диапазон ${number(card.economy.drrMin == null ? null : card.economy.drrMin * 100)} — ${number(card.economy.drrMax == null ? null : card.economy.drrMax * 100)}% · с одной дополнительной единицей ${number(card.economy.drrPlusOne == null ? null : card.economy.drrPlusOne * 100)}%`}</Typography>
    </>}
    <Typography fontWeight={700}>{p?.primary.label ?? card.rule.explanation}</Typography>
    {p?.alternatives.map(a => <Typography key={a.kind} variant="body2">или {a.label.toLocaleLowerCase('ru-RU')}</Typography>)}
    {p?.warning ? <Typography variant="body2">{p.warning}</Typography> : null}
    {p?.restrictions.map(r => <Typography key={r.code} variant="body2" color="text.secondary">{r.label}</Typography>)}
    {p?.diagnostics.map(d => <Tooltip key={d.code} title={d.basis}><Typography variant="body2">{d.label}</Typography></Tooltip>)}
    {p && card.subject.scope === 'cluster' ? <Typography variant="body2">Расход кластера ≈ {formatMoney(p.weeklySpend)} в неделю</Typography> : null}
    {p?.control?.receivedAtUtc ? <Typography variant="caption" color="text.secondary">Настройки WB получены {new Date(p.control.receivedAtUtc).toLocaleString('ru-RU', { timeZone: 'Europe/Moscow' })} МСК.</Typography> : null}
    <Typography variant="caption" color="text.secondary">{!details ? 'Сумма заказов кластера оценена по средней цене единицы товара за фиксированные 14 дней. ' : ''}Данные WB могут уточняться, в том числе после D−2.</Typography>
    {card.decision?.reason ? <Typography variant="body2">Причина: {card.decision.reason}</Typography> : null}
    {card.decision?.comment ? <Typography variant="body2">Комментарий: {card.decision.comment}</Typography> : null}
    {showDecisionButtons && runId && p ? <DecisionButtons campaignId={card.subject.campaignId} runId={runId} evaluationIds={[card.evaluationId]} ruleId={card.rule.ruleId} /> : null}
  </Stack>;
}
