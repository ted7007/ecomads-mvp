import { Accordion, AccordionDetails, AccordionSummary, Alert, Box, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { getResultDetails, resultDate, type ResultMetrics } from './resultsApi';
import { formatMoney } from '../../shared/lib/formatMoney';
import { moscowToday } from '../DashboardPage/components/PeriodFilter';

function Metrics({ title, data, start, end }: { title: string; data: ResultMetrics; start: string; end: string }) {
  return <Stack spacing={.5} sx={{ p: 1.5, borderRadius: 2, bgcolor: 'action.hover', minWidth: 0 }}>
    <Typography fontWeight={700}>{title}</Typography><Typography variant="caption">{resultDate(start)} — {resultDate(end)}, МСК</Typography>
    <Typography variant="body2">Расход кампании: {formatMoney(data.campaignSpend)}</Typography>
    <Typography variant="body2">ДРР кампании: {data.campaignDrr == null ? '—' : `${(data.campaignDrr * 100).toFixed(1)}%`}</Typography>
    {data.objects.map(o => <Box key={o.key} sx={{ pt: .5 }}><Typography variant="body2" fontWeight={600}>Артикул {o.article}{o.key.split(':')[1] ? ` · ${o.key.slice(o.key.indexOf(':') + 1)}` : ''}</Typography>
      <Typography variant="body2">Расход {formatMoney(o.spend)} · {o.clicks} кликов · {o.units} рекламных единиц</Typography>
      <Typography variant="body2">Все заказанные единицы товара: {o.totalUnits ?? '—'}</Typography>
      <Typography variant="body2">Средняя цена: {o.unitPrice == null ? '—' : formatMoney(o.unitPrice)}</Typography>
      <Typography variant="caption">Остатки известны за {o.knownStockDays} из 7 дней · без остатка {o.zeroStockDays} дн.</Typography></Box>)}
    {data.gaps.map(g => <Typography key={g} variant="caption" color="warning.main">{g}</Typography>)}
  </Stack>;
}
export function ResultDetails({ id }: { id: string }) {
  const query = useQuery({ queryKey: ['wb-results-detail', id], queryFn: () => getResultDetails(id) });
  if (query.isPending) return <Typography role="status">Загружаем подробности…</Typography>;
  if (query.isError) return <Alert severity="error" onClose={() => void query.refetch()}>Не удалось загрузить расчёт. Закройте сообщение, чтобы повторить.</Alert>;
  const d = query.data; const current = d.revisions[0];
  const sources: Record<string, string> = { settings: 'минусы и ставки', advertising: 'настройки кампании', budgets: 'бюджет кампании' };
  return <Stack spacing={1.5} sx={{ overflowWrap: 'anywhere' }}>
    <Typography variant="body2">{d.row.reason}</Typography>
    {d.row.members.map(m => <Typography key={m.recommendationId} variant="body2">{m.ruleId} · {m.object} · {m.action}{m.reason ? ` · причина решения: ${m.reason}` : ''}{m.snoozeUntil ? ` · отложено до ${resultDate(m.snoozeUntil)}` : ''}</Typography>)}
    {current && d.beforeWindow && d.afterWindow && d.afterWindow.end < moscowToday() && d.row.status !== 'done' ? <>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 1.5 }}>
        <Metrics title="7 дней до" data={current.evidence.before} start={d.beforeWindow.start} end={d.beforeWindow.end} />
        <Metrics title="7 дней после" data={current.evidence.after} start={d.afterWindow.start} end={d.afterWindow.end} />
      </Box><Typography fontWeight={700}>Как посчитано</Typography><Typography variant="body2">{current.evidence.outcome.formula}</Typography>
      <Typography variant="body2">{current.evidence.outcome.reason}</Typography>
    </> : <Typography variant="body2">{d.row.status === 'historical' ? 'Старая отметка сохранена. Замер эффекта для неё не запускался.' : d.row.status === 'done' ? 'Выполнение подтверждено по API. Результат не оценивается в рублях.' : d.beforeWindow && d.afterWindow ? `Окна: ${resultDate(d.beforeWindow.start)} — ${resultDate(d.beforeWindow.end)} и ${resultDate(d.afterWindow.start)} — ${resultDate(d.afterWindow.end)}. Итог ожидается около ${resultDate(d.row.expectedDate)}.` : 'Окна сравнения появятся после подтверждения применения.'}</Typography>}
    {d.applications.map(a => <Typography key={a.recommendationId} variant="caption" color="text.secondary">Изменение обнаружено между {new Date(a.before.atUtc).toLocaleString('ru-RU', { timeZone: 'Europe/Moscow' })} и {new Date(a.applied.atUtc).toLocaleString('ru-RU', { timeZone: 'Europe/Moscow' })} МСК. Источник: {sources[a.applied.source] ?? a.applied.source}. Дни между снимками исключены из сравнения.</Typography>)}
    {d.revisions.length > 0 ? <Accordion disableGutters elevation={0}><AccordionSummary expandIcon="⌄">Версии расчёта · {d.revisions.length}</AccordionSummary><AccordionDetails><Stack spacing={1}>{d.revisions.map(r => <Typography key={r.number} variant="body2">Версия {r.number} · {new Date(r.calculatedAtUtc).toLocaleString('ru-RU', { timeZone: 'Europe/Moscow' })} МСК · {r.evidence.outcome.reason} · {r.evidence.outcome.savings == null ? 'без денежной оценки' : formatMoney(r.evidence.outcome.savings)}</Typography>)}</Stack></AccordionDetails></Accordion> : null}
    <Typography variant="caption" color="text.secondary">Показано изменение после применения. Сравнение до/после не отделяет все внешние причины. Данные WB могут уточняться.</Typography>
  </Stack>;
}
