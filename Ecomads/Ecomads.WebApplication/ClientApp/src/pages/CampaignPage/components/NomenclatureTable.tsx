import { Stack, Tooltip, Typography } from '@mui/material';
import type { NomenclatureStatistics as Row } from '../campaignApi';
import { formatMoney as money } from '../../../shared/lib/formatMoney';
import { formatPercent as percent } from '../../../shared/lib/formatPercent';
import { EmptyState } from '../../../shared/ui/EmptyState';
import { ReportTable, type ReportColumn } from './ReportTable';
const drr = (r: Row) => r.revenue > 0 ? r.spend / r.revenue * 100 : null;
const unavailable = <Tooltip title="Сумма заказов товара по всем каналам пока недоступна"><span>—</span></Tooltip>;
export function NomenclatureTable({ rows, resetKey }: { rows: Row[]; resetKey?: string }) {
  const name = (r: Row) => <><Typography sx={{overflowWrap: 'anywhere'}} fontWeight={700}>{r.name || r.nomenclatureId}</Typography><Typography variant="caption" color="text.secondary">Арт. {r.nomenclatureId}</Typography></>;
  const columns: ReportColumn<Row>[] = [
    { key: 'name', label: 'Товар', text: true, width: '26%', value: r => r.name, render: name },
    { key: 'spend', label: 'Расход, ₽', value: r => r.spend, render: r => money(r.spend) },
    { key: 'revenue', label: 'Рекламная сумма заказов, ₽', value: r => r.revenue, render: r => money(r.revenue) },
    { key: 'drr', label: 'Рекламный ДРР, %', value: drr, render: r => percent(drr(r), 1) },
    { key: 'total', label: 'Все заказы товара, ₽', sortable: false, value: () => null, render: () => unavailable },
    { key: 'totalDrr', label: 'Общий ДРР, %', sortable: false, value: () => null, render: () => unavailable }];
  if (!rows.length) return <EmptyState title="Нет данных по товарам за выбранный период" />;
  return <ReportTable rows={rows} columns={columns} rowId={r => r.nomenclatureId} label="Товары в кампании" resetKey={resetKey}
    mobile={r => <Stack spacing={.5}>{name(r)}<Typography>Расход: {money(r.spend)} · Заказы: {money(r.revenue)}</Typography><Typography>ДРР: {percent(drr(r), 1)}</Typography></Stack>}
    details={r => <Stack spacing={1}>{name(r)}<Typography>Все заказы товара: {unavailable} · Общий ДРР: {unavailable}</Typography>{[['Показы', r.impressions], ['Клики', r.clicks], ['Корзины', r.carts], ['Заказы', r.orders], ['CTR', percent(r.ctr, 2)], ['CR', percent(r.cr, 2)], ['CPC', money(r.cpc)], ['CPO', money(r.cpo)]].map(([k, v]) => <Typography key={k}>{k}: {v}</Typography>)}</Stack>} />;
}
