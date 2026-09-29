import { Table, TableBody, TableCell, TableContainer, TableHead, TableRow } from '@mui/material';
import type { NomenclatureStatistics } from '../campaignApi';
import { formatMoney } from '../../../shared/lib/formatMoney';
import { formatPercent } from '../../../shared/lib/formatPercent';
import { EmptyState } from '../../../shared/ui/EmptyState';

export function NomenclatureTable({ rows }: { rows: NomenclatureStatistics[] }) {
  if (!rows.length) return <EmptyState title="Нет данных по товарам за выбранный период" />;
  return <TableContainer sx={{ overflowX: 'auto' }}><Table size="small" sx={{ minWidth: 1050 }}><TableHead><TableRow>
    {['Номенклатура', 'Показы', 'Клики', 'Корзины', 'Заказы', 'CTR', 'CR', 'Затраты', 'Сумма заказов', 'CPC', 'CPO'].map((label) => <TableCell key={label} align={label === 'Номенклатура' ? 'left' : 'right'} sx={{ fontWeight: 800 }}>{label}</TableCell>)}
  </TableRow></TableHead><TableBody>{rows.map((row) => <TableRow key={row.nomenclatureId}>
    <TableCell>{row.name}<br /><small>{row.nomenclatureId}</small></TableCell><TableCell align="right">{row.impressions.toLocaleString('ru-RU')}</TableCell><TableCell align="right">{row.clicks.toLocaleString('ru-RU')}</TableCell><TableCell align="right">{row.carts.toLocaleString('ru-RU')}</TableCell><TableCell align="right">{row.orders.toLocaleString('ru-RU')}</TableCell><TableCell align="right">{formatPercent(row.ctr, 2)}</TableCell><TableCell align="right">{formatPercent(row.cr, 2)}</TableCell><TableCell align="right">{formatMoney(row.spend)}</TableCell><TableCell align="right">{formatMoney(row.revenue)}</TableCell><TableCell align="right">{row.cpc == null ? '—' : formatMoney(row.cpc)}</TableCell><TableCell align="right">{row.cpo == null ? '—' : formatMoney(row.cpo)}</TableCell>
  </TableRow>)}</TableBody></Table></TableContainer>;
}
