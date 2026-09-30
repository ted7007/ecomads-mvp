import { Box, Button, Collapse, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import { Fragment, useEffect, useState } from 'react';
import type { NomenclatureStatistics } from '../campaignApi';
import { formatMoney } from '../../../shared/lib/formatMoney';
import { formatPercent } from '../../../shared/lib/formatPercent';
import { EmptyState } from '../../../shared/ui/EmptyState';
import { PaginationBar } from '../../../shared/ui/PaginationBar';

export function NomenclatureTable({ rows }: { rows: NomenclatureStatistics[] }) {
  const [page, setPage] = useState(0);
  const [rowsPerPage, setRowsPerPage] = useState(5);
  const [expanded, setExpanded] = useState<string | null>(null);
  useEffect(() => { setPage(0); setExpanded(null); }, [rows]);
  if (!rows.length) return <EmptyState title="Нет данных по товарам за выбранный период" />;
  return <><TableContainer sx={{ overflowX: 'auto' }}><Table size="small" sx={{ minWidth: 720 }}><TableHead><TableRow>
    <TableCell>Товар</TableCell><TableCell align="right">Расход</TableCell><TableCell align="right">Рекламная сумма заказов</TableCell><TableCell align="right">Рекламный ДРР</TableCell><TableCell align="right">CPC</TableCell><TableCell align="right">Ещё</TableCell>
  </TableRow></TableHead><TableBody>{rows.slice(page * rowsPerPage, (page + 1) * rowsPerPage).map((row) => {
    const open = expanded === row.nomenclatureId;
    const name = row.name?.trim() || row.nomenclatureId;
    return <Fragment key={row.nomenclatureId}><TableRow hover>
      <TableCell><Typography fontWeight={700}>{name}</Typography>{name !== row.nomenclatureId ? <Typography variant="caption" color="text.secondary">Арт. {row.nomenclatureId}</Typography> : null}</TableCell>
      <TableCell align="right">{formatMoney(row.spend)}</TableCell><TableCell align="right">{formatMoney(row.revenue)}</TableCell>
      <TableCell align="right">{row.revenue > 0 ? formatPercent(row.spend / row.revenue * 100, 1) : '—'}</TableCell>
      <TableCell align="right">{row.cpc == null ? '—' : formatMoney(row.cpc)}</TableCell><TableCell align="right"><Button size="small" aria-expanded={open} onClick={() => setExpanded(open ? null : row.nomenclatureId)}>{open ? 'Скрыть' : 'Показать'}</Button></TableCell>
    </TableRow><TableRow><TableCell colSpan={6} sx={{ p: open ? 1.5 : 0, borderBottom: open ? undefined : 0 }}><Collapse in={open} unmountOnExit><Box><Stack direction="row" useFlexGap flexWrap="wrap" gap={2}>
      <Typography variant="body2">Показы: {row.impressions.toLocaleString('ru-RU')}</Typography><Typography variant="body2">Клики: {row.clicks.toLocaleString('ru-RU')}</Typography><Typography variant="body2">Корзины: {row.carts.toLocaleString('ru-RU')}</Typography><Typography variant="body2">Заказы: {row.orders.toLocaleString('ru-RU')}</Typography><Typography variant="body2">CTR: {formatPercent(row.ctr, 2)}</Typography><Typography variant="body2">CR: {formatPercent(row.cr, 2)}</Typography><Typography variant="body2">CPO: {row.cpo == null ? '—' : formatMoney(row.cpo)}</Typography>
    </Stack></Box></Collapse></TableCell></TableRow></Fragment>;
  })}</TableBody></Table></TableContainer>
    <PaginationBar count={rows.length} page={page} rowsPerPage={rowsPerPage} onPageChange={setPage} onRowsPerPageChange={(size) => { setRowsPerPage(size); setPage(0); }} />
  </>;
}
