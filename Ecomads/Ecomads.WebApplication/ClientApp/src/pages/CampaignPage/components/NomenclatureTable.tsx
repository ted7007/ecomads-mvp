import { Table, TableBody, TableCell, TableContainer, TableHead, TablePagination, TableRow, Typography } from '@mui/material';
import { useEffect, useState } from 'react';
import type { NomenclatureStatistics } from '../campaignApi';
import { formatMoney } from '../../../shared/lib/formatMoney';
import { formatPercent } from '../../../shared/lib/formatPercent';
import { EmptyState } from '../../../shared/ui/EmptyState';

export function NomenclatureTable({ rows }: { rows: NomenclatureStatistics[] }) {
  const [page, setPage] = useState(0);
  const [rowsPerPage, setRowsPerPage] = useState(10);
  useEffect(() => setPage(0), [rows]);
  if (!rows.length) return <EmptyState title="Нет данных по товарам за выбранный период" />;
  return <><Typography variant="caption" color="text.secondary" sx={{ display: { xs: 'block', md: 'none' }, mb: 1 }}>
    Прокрутите таблицу вправо, чтобы увидеть остальные показатели.
  </Typography><TableContainer sx={{ overflowX: 'auto' }}><Table size="small" sx={{ minWidth: 1050 }}><TableHead><TableRow>
    {['Номенклатура', 'Показы', 'Клики', 'Корзины', 'Заказы', 'CTR', 'CR', 'Затраты', 'Сумма заказов', 'CPC', 'CPO'].map((label) => <TableCell key={label} align={label === 'Номенклатура' ? 'left' : 'right'} sx={{ fontWeight: 800 }}>{label}</TableCell>)}
  </TableRow></TableHead><TableBody>{rows.slice(page * rowsPerPage, (page + 1) * rowsPerPage).map((row) => <TableRow key={row.nomenclatureId}>
    <TableCell>{row.name}<br /><small>{row.nomenclatureId}</small></TableCell><TableCell align="right">{row.impressions.toLocaleString('ru-RU')}</TableCell><TableCell align="right">{row.clicks.toLocaleString('ru-RU')}</TableCell><TableCell align="right">{row.carts.toLocaleString('ru-RU')}</TableCell><TableCell align="right">{row.orders.toLocaleString('ru-RU')}</TableCell><TableCell align="right">{formatPercent(row.ctr, 2)}</TableCell><TableCell align="right">{formatPercent(row.cr, 2)}</TableCell><TableCell align="right">{formatMoney(row.spend)}</TableCell><TableCell align="right">{formatMoney(row.revenue)}</TableCell><TableCell align="right">{row.cpc == null ? '—' : formatMoney(row.cpc)}</TableCell><TableCell align="right">{row.cpo == null ? '—' : formatMoney(row.cpo)}</TableCell>
  </TableRow>)}</TableBody></Table></TableContainer>
    <TablePagination component="div" count={rows.length} page={page} rowsPerPage={rowsPerPage}
      onPageChange={(_, nextPage) => setPage(nextPage)}
      onRowsPerPageChange={(event) => { setRowsPerPage(Number(event.target.value)); setPage(0); }}
      rowsPerPageOptions={[5, 10, 20, 50]} labelRowsPerPage="Строк на странице"
      labelDisplayedRows={({ from, to, count }) => `${from}–${to} из ${count}`}
      sx={{ mt: 1, '& .MuiTablePagination-toolbar': { px: 0, flexWrap: 'wrap', justifyContent: 'flex-end' } }} />
  </>;
}
