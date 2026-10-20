import { Fragment, useState } from 'react';
import { Alert, Box, Button, ButtonBase, Card, CardContent, Chip, Collapse, LinearProgress, MenuItem, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, ToggleButton, ToggleButtonGroup, Typography, useMediaQuery, useTheme } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Link, useSearchParams } from 'react-router-dom';
import { getResults, resultDate, resultLabels, type ResultRow, type ResultsReport } from './resultsApi';
import { getWbStores } from '../WbStoresPage/wbStoresApi';
import { appRoutes } from '../../app/routes';
import { PageHeader } from '../../shared/ui/PageHeader';
import { PaginationBar } from '../../shared/ui/PaginationBar';
import { formatMoney } from '../../shared/lib/formatMoney';
import { ResultDetails } from './ResultDetails';

const filters = [['all', 'Все'], ['result', 'С результатом'], ['measuring', 'На замере'], ['not_applied', 'Не применено'], ['rejected', 'Отклонено и отложено']];
function Badge({ row }: { row: ResultRow }) { return <Chip size="small" variant="outlined" color={row.status === 'confirmed' ? 'success' : row.status === 'measuring' ? 'info' : ['not_confirmed', 'not_applied'].includes(row.status) ? 'warning' : 'default'} label={resultLabels[row.status]} />; }
function Journal({ rows }: { rows: ResultRow[] }) {
  const [open, setOpen] = useState<string | null>(null); const mobile = useMediaQuery(useTheme().breakpoints.down('md'));
  const decisions: Record<string, string> = { accepted: 'Принял', snoozed: 'Отложено', irrelevant: 'Не актуально' };
  const decision = (m: ResultRow['members'][number]) => [m.decision ? decisions[m.decision] ?? m.decision : 'Без решения', m.reason,
    m.snoozeUntil ? `до ${resultDate(m.snoozeUntil)}` : null].filter(Boolean).join(' · ');
  const subject = (r: ResultRow) => r.members.length > 1 ? `${r.members.length} действия · ${r.members.map(m => m.object).join(', ')}` : r.members[0]?.object;
  const toggle = (id: string) => setOpen(old => old === id ? null : id);
  if (mobile) return <Stack spacing={1.5}>{rows.map(r => <Box key={r.id} sx={{ border: 1, borderColor: 'divider', borderRadius: 2, p: 1.5 }}>
    <ButtonBase onClick={() => toggle(r.id)} aria-expanded={open === r.id} aria-label={`Раскрыть результат: ${subject(r)}`} sx={{ display: 'block', width: '100%', textAlign: 'left' }}><Stack spacing={.75}>
      <Typography variant="caption" color="text.secondary">{resultDate(r.members[0]?.firstShownAtUtc ?? null)} · {r.campaign}</Typography>
      <Typography fontWeight={700} sx={{ overflowWrap: 'anywhere' }}>{subject(r)}</Typography><Typography variant="body2">{r.members.map(m => m.action).join(' · ')}</Typography>
      <Typography variant="body2">Решение: {r.members.map(decision).join(' · ')}</Typography>
      <Typography variant="caption">Применено: {resultDate(r.appliedDate)}</Typography><Box><Badge row={r} /></Box>
      <Typography fontWeight={700} color={r.status === 'confirmed' ? 'success.main' : 'text.primary'}>{r.savings == null ? '—' : formatMoney(r.savings)} <Typography component="span" variant="caption">{open === r.id ? 'Скрыть ↑' : 'Как посчитано ↓'}</Typography></Typography>
    </Stack></ButtonBase><Collapse in={open === r.id} unmountOnExit><Box sx={{ pt: 2 }}><ResultDetails id={r.id} /></Box></Collapse></Box>)}</Stack>;
  return <TableContainer><Table size="small" aria-label="Журнал действий"><TableHead><TableRow>{['Дата', 'Кампания · объект', 'Рекомендация', 'Ваше решение', 'Применено', 'Результат', 'Эффект'].map(h => <TableCell key={h}>{h}</TableCell>)}</TableRow></TableHead><TableBody>
    {rows.map(r => <Fragment key={r.id}><TableRow hover>
      <TableCell sx={{ whiteSpace: 'nowrap' }}>{resultDate(r.members[0]?.firstShownAtUtc ?? null)}</TableCell>
      <TableCell><ButtonBase onClick={() => toggle(r.id)} aria-expanded={open === r.id} aria-label={`Раскрыть результат: ${subject(r)}`} sx={{ display: 'block', textAlign: 'left', overflowWrap: 'anywhere' }}><Typography variant="body2" fontWeight={700}>{subject(r)}</Typography><Typography variant="caption">{r.campaign} · {open === r.id ? 'Скрыть ↑' : 'Как посчитано ↓'}</Typography></ButtonBase></TableCell>
      <TableCell>{r.members.map(m => m.action).join(' · ')}</TableCell><TableCell>{r.members.map(decision).join(' · ')}</TableCell>
      <TableCell sx={{ whiteSpace: 'nowrap' }}>{resultDate(r.appliedDate)}</TableCell><TableCell><Badge row={r} /></TableCell><TableCell sx={{ whiteSpace: 'nowrap', fontWeight: 700, color: r.status === 'confirmed' ? 'success.main' : undefined }}>{r.savings == null ? '—' : formatMoney(r.savings)}</TableCell>
    </TableRow>{open === r.id ? <TableRow><TableCell colSpan={7} sx={{ p: 2.5 }}><ResultDetails id={r.id} /></TableCell></TableRow> : null}</Fragment>)}
  </TableBody></Table></TableContainer>;
}
function SavingsChart({ data }: { data: ResultsReport }) {
  if (!data.weeks.length) return <Stack spacing={1} sx={{ minHeight: 180, justifyContent: 'center' }}><Typography color="text.secondary">Подтверждённых замеров пока нет</Typography><Typography variant="caption">Точка появится после завершения семидневного замера. Ориентир — около десятого дня после обнаружения применения.</Typography></Stack>;
  const max = Math.max(1, ...data.weeks.map(w => w.cumulative));
  const coords = data.weeks.map((w, i) => [50 + i * 500 / Math.max(1, data.weeks.length - 1), 165 - w.cumulative / max * 130]);
  return <Box component="svg" viewBox="0 0 600 220" role="img" aria-label="Накопленная подтверждённая экономия по неделям" sx={{ width: '100%', minHeight: 180 }}>
    {[0, .5, 1].map(r => <g key={r}><line x1={50} x2={550} y1={165 - r * 130} y2={165 - r * 130} stroke="currentColor" opacity={.12} /><text x={2} y={170 - r * 130} fontSize={11} fill="currentColor">{Math.round(max * r).toLocaleString('ru-RU')}</text></g>)}
    <polyline points={coords.map(c => c.join(',')).join(' ')} fill="none" stroke="#1A7F45" strokeWidth={3} />
    {data.weeks.map((w, i) => <g key={w.start}><circle cx={coords[i][0]} cy={coords[i][1]} r={4} fill="#1A7F45"><title>{resultDate(w.start)}: {formatMoney(w.cumulative)}</title></circle>{(i === 0 || i === data.weeks.length - 1 || data.weeks.length <= 5) ? <text x={coords[i][0]} y={195} fontSize={11} fill="currentColor" textAnchor="middle">{resultDate(w.start).slice(0, 5)}</text> : null}</g>)}
  </Box>;
}
export function ResultsPage() {
  const [params, setParams] = useSearchParams();
  const period = ['month', '30', 'all'].includes(params.get('period') ?? '') ? params.get('period')! : 'month';
  const filter = filters.some(f => f[0] === params.get('filter')) ? params.get('filter')! : 'all';
  const page = Math.max(0, Number(params.get('page')) || 0); const pageSize = [5, 10, 20, 100].includes(Number(params.get('pageSize'))) ? Number(params.get('pageSize')) : 10;
  const storeId = params.get('storeId') || undefined;
  const stores = useQuery({ queryKey: ['wb-stores'], queryFn: getWbStores });
  const query = useQuery({ queryKey: ['wb-results', { storeId, period, filter, page, pageSize }], queryFn: () => getResults({ storeId, period, filter, page, pageSize }), refetchInterval: 30000 });
  function set(key: string, value: string, reset = true) { const next = new URLSearchParams(params); next.set(key, value); if (reset) next.delete('page'); setParams(next); }
  const d = query.data;
  const delta = d?.summary.ordersBefore != null && d.summary.ordersAfter != null ? d.summary.ordersAfter - d.summary.ordersBefore : null;
  return <Stack spacing={2.5}>
    <PageHeader title="Результаты" description="Что изменилось после применения рекомендаций EcomAds" actions={<ToggleButtonGroup exclusive size="small" value={period} onChange={(_, v) => v && set('period', v)} aria-label="Период результатов"><ToggleButton value="month">Месяц</ToggleButton><ToggleButton value="30">30 дней</ToggleButton><ToggleButton value="all">Всё время</ToggleButton></ToggleButtonGroup>} />
    {stores.isError ? <Alert severity="error">Не удалось загрузить кабинеты</Alert> : stores.data?.length === 0 ? <Alert severity="info" action={<Button component={Link} to={appRoutes.wbStores}>Подключить кабинет WB</Button>}>Подключите кабинет, чтобы собирать историю изменений.</Alert> : <TextField select size="small" SelectProps={{ displayEmpty: true }} InputLabelProps={{ shrink: true }} label="Кабинет WB" value={storeId ?? ''} onChange={e => set('storeId', e.target.value)} sx={{ maxWidth: 360 }}><MenuItem value="">Все кабинеты</MenuItem>{stores.data?.map(s => <MenuItem key={s.id} value={s.id}>{s.name}</MenuItem>)}</TextField>}
    {query.isPending ? <Typography role="status">Загружаем результаты…</Typography> : null}
    {query.isError ? <Alert severity="error" action={<Button onClick={() => void query.refetch()}>Повторить</Button>}>Не удалось загрузить результаты. Проверьте соединение и выбранный кабинет.</Alert> : null}
    {d ? <>
      {!d.enabled ? <Alert severity="info">Новые замеры отключены. Сохранённая история доступна.</Alert> : null}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1.3fr 1fr 1fr' }, gap: 1.75 }}>
        <Card sx={{ bgcolor: 'rgba(52,199,89,.12)', borderColor: 'rgba(52,199,89,.35)' }}><CardContent><Stack spacing={.75}><Typography component="h2" variant="body1" fontWeight={600}>Сэкономлено на рекламе</Typography><Typography sx={{ fontSize: 36, fontWeight: 800, color: 'success.main', fontVariantNumeric: 'tabular-nums' }}>{d.summary.confirmedMeasurements ? formatMoney(d.summary.savings) : '—'}</Typography><Typography variant="body2">{d.summary.confirmedMeasurements ? `Подтверждённых рекомендаций: ${d.summary.confirmedRecommendations} · замеров: ${d.summary.confirmedMeasurements}. Измерено за 7 дней.` : 'Пока нет подтверждённой экономии'}</Typography>
          {d.summary.forecast > 0 ? <Typography variant="caption" color="text.secondary">Если действия сохраняются — ещё ≈ {formatMoney(d.summary.forecast)} за месяц. В итог не входит.</Typography> : null}
          <Typography variant="caption">На замере: {d.summary.measuring}. Ожидаемые суммы в итог не включены.</Typography></Stack></CardContent></Card>
        <Card><CardContent><Typography component="h2" fontWeight={600}>Рост от масштабирования</Typography><Typography sx={{ fontSize: 28, py: 1 }}>—</Typography><Typography variant="body2" color="text.secondary">Появится после реализации рекомендаций по масштабированию. Эффект устранения дублирования органики также пока недоступен.</Typography></CardContent></Card>
        <Card><CardContent><Typography component="h2" fontWeight={600}>Заказы по изменённым объектам</Typography><Typography sx={{ fontSize: 28, py: 1, fontWeight: 700 }}>{delta == null ? '—' : `${delta > 0 ? '+' : ''}${delta} ед.`}</Typography><Typography variant="body2" color="text.secondary">{d.summary.ordersReason ?? `Рекламные единицы: ${d.summary.ordersBefore} → ${d.summary.ordersAfter}. Только подтверждённые замеры.`}</Typography></CardContent></Card>
      </Box>
      <Typography variant="caption" color="text.secondary">Экономия и график — по дате подтверждения результата. Журнал и воронка — по первому показу рекомендации. Все даты — МСК.</Typography>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '1fr 1.15fr' }, gap: 1.75 }}>
        <Card><CardContent><Stack spacing={1.5}><Typography component="h2" variant="h6">Что происходило с рекомендациями</Typography>
          {[[d.funnel.issued, 'Выдано рекомендаций'], [d.funnel.accepted, 'Принято'], [d.funnel.applied, 'Применено в WB'], [d.funnel.confirmed, 'Эффект подтверждён']].map(([n, label]) => <Box key={label}><Stack direction="row" justifyContent="space-between" gap={1}><Typography variant="body2">{label}</Typography><Typography variant="body2" fontWeight={700}>{n}{d.funnel.issued && label !== 'Выдано рекомендаций' ? ` · ${Math.round(Number(n) / d.funnel.issued * 100)}% от выданных` : ''}</Typography></Stack><LinearProgress variant="determinate" value={d.funnel.issued ? Number(n) / d.funnel.issued * 100 : 0} sx={{ height: 8, borderRadius: 5, mt: .5 }} /></Box>)}
          <Typography variant="caption">Применено без «Принял»: {d.funnel.appliedWithoutAcceptance} · Отложено: {d.funnel.snoozed} · Не актуально: {d.funnel.irrelevant} · Без решения: {d.funnel.undecided}</Typography>
          {Object.entries(d.funnel.rejectionReasons).map(([reason, n]) => <Typography key={reason} variant="caption">Не актуально: {reason} — {n}</Typography>)}
        </Stack></CardContent></Card>
        <Card><CardContent><Typography component="h2" variant="h6">Накопленная экономия по неделям</Typography><SavingsChart data={d} /><Typography variant="caption" color="text.secondary">Только измеренное за семь дней. {d.summary.measuring ? `На замере: ${d.summary.measuring}; они пока не добавлены к графику.` : 'Прогноз в подтверждённую линию не входит.'}</Typography></CardContent></Card>
      </Box>
      <Card><CardContent><Stack spacing={2}><Typography component="h2" variant="h6">Журнал действий</Typography><Stack direction="row" gap={.75} useFlexGap flexWrap="wrap">{filters.map(([key, label]) => <Button key={key} variant={filter === key ? 'contained' : 'outlined'} size="small" aria-pressed={filter === key} onClick={() => set('filter', key)}>{label}</Button>)}</Stack>
        {d.rows.length ? <Journal key={`${storeId}:${period}:${filter}:${d.page}`} rows={d.rows} /> : <Stack spacing={1} sx={{ py: 3 }}><Typography>{filter === 'all' ? 'История результатов пока накапливается' : 'Нет действий с выбранным результатом'}</Typography><Typography variant="body2" color="text.secondary">{d.collectionStartedAtUtc ? `Наблюдения сохраняются с ${resultDate(d.collectionStartedAtUtc)}. Для результата нужны исходные снимки и семь полных дней после применения.` : 'История начнётся с показанных рекомендаций и новых снимков настроек WB. Старые решения без доказательств применения не дают экономию задним числом.'}</Typography></Stack>}
        <PaginationBar desktopSizeButtons count={d.total} page={d.page} rowsPerPage={pageSize} onPageChange={p => set('page', String(p), false)} onRowsPerPageChange={n => set('pageSize', String(n))} />
      </Stack></CardContent></Card>
      <Typography variant="caption" color="text.secondary">Смешанные пакеты и действия без согласованной методики показываются без денежной оценки. Результаты по остаткам появятся при включённом R01 и достаточной истории FBW/FBS.</Typography>
    </> : null}
  </Stack>;
}
