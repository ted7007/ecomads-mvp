import { Accordion, AccordionDetails, AccordionSummary, Alert, MenuItem, Select, Stack, Typography } from '@mui/material';
import type { WbJamResponse, WbJamRow as Row } from '../campaignApi';
import { ReportTable, type ReportColumn } from './ReportTable';
import { jamCandidates, pairKey } from './campaignReportModel';
const count = (v: number | null) => v == null ? '—' : v.toLocaleString('ru-RU', { maximumFractionDigits: 1 });
export function WbJamTable({ report, onSelectPeriod }: { report: WbJamResponse; onSelectPeriod: (start: string, end: string) => void }) {
  const name = (r: Row) => <><Typography sx={{overflowWrap: 'anywhere'}} fontWeight={700}>{r.searchText}</Typography><Typography variant="caption">Арт. {r.nomenclatureId}</Typography></>;
  const columns: ReportColumn<Row>[] = [{ key: 'name', label: 'Запрос / артикул', text: true, width: '40%', value: r => r.searchText, render: name },
  { key: 'orders', label: 'Заказы', value: r => r.orders, render: r => count(r.orders) }, { key: 'frequency', label: 'Частотность', value: r => r.frequency, render: r => count(r.frequency) }, { key: 'position', label: 'Позиция · Джем', value: r => r.averagePosition, render: r => count(r.averagePosition) }];
  const mobile = (r: Row) => <Stack spacing={.5}>{name(r)}<Typography>Заказы: {count(r.orders)} · Частотность: {count(r.frequency)} · Позиция: {count(r.averagePosition)}</Typography></Stack>;
  const render = (rows: Row[], full = false) => <ReportTable rows={rows} columns={full ? [...columns, { key: 'opens', label: 'Переходы', value: r => r.openCard, render: r => count(r.openCard) }, { key: 'carts', label: 'Корзины', value: r => r.addToCart, render: r => count(r.addToCart) }, { key: 'match', label: 'Кластер загружен', text: true, value: r => Number(r.matchingLoadedAdCluster), render: r => r.matchingLoadedAdCluster ? 'Совпадает' : '—' }] : columns} defaultSort="orders" rowId={r => pairKey(r.nomenclatureId, r.searchText)} label={full ? 'Полный отчёт Джема' : 'Запросы с заказами'} resetKey={`${report.startDate}:${report.endDate}`} mobile={mobile} details={full ? r => <Stack>{mobile(r)}<Typography>Переходы: {count(r.openCard)} · Корзины: {count(r.addToCart)}</Typography><Typography>Недельная частотность: {count(r.weekFrequency)} · Медианная позиция: {count(r.medianPosition)}</Typography></Stack> : undefined} />;
  const candidates = jamCandidates(report.rows);
  const current = `${report.startDate}/${report.endDate}`;
  const periods = [{ startDate: report.startDate, endDate: report.endDate }, ...report.availableReports.filter(p => `${p.startDate}/${p.endDate}` !== current)];
  return <Stack spacing={1.5}><Typography variant="caption" color="text.secondary">Джем · {report.startDate} — {report.endDate} · Позиция в поиске; органическая позиция не подтверждена.</Typography>
    {report.reportStatus === 'access_denied' ? <Alert severity="warning">Нет доступа к Джему. Проверьте права токена и подписку.</Alert> : null}
    {candidates.length ? render(candidates) : <Typography color="text.secondary">{report.reportStatus === 'not_loaded' ? 'Отчёт Джема ещё не загружен.' : 'Запросов с заказами без совпадения с загруженными кластерами нет.'}</Typography>}
    <Accordion disableGutters elevation={0}><AccordionSummary expandIcon="⌄"><Typography>Полный отчёт Джема · {report.rows.length} запросов</Typography></AccordionSummary><AccordionDetails><Stack spacing={2}>
      <Select size="small" value={current} inputProps={{ 'aria-label': 'Период отчёта Джема' }} onChange={e => { const [s, t] = e.target.value.split('/'); onSelectPeriod(s, t); }}>{periods.map(p => <MenuItem key={`${p.startDate}/${p.endDate}`} value={`${p.startDate}/${p.endDate}`}>{p.startDate} — {p.endDate}</MenuItem>)}</Select>
      <Typography variant="body2" color="text.secondary">WB возвращает до 30 запросов на товар по заказам. Совпадение означает совпадение текста среди загруженных кластеров. Проверено {report.checkedArticleCount} из {report.articleCount} товаров. Статус: {report.reportStatus}.</Typography>{render(report.rows, true)}
    </Stack></AccordionDetails></Accordion></Stack>;
}
