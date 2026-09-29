import { Alert, Table, TableBody, TableCell, TableContainer, TableHead, TablePagination, TableRow, Typography } from '@mui/material';
import { useEffect, useState } from 'react';
import type { WbClusterRow } from '../campaignApi';

const money = (value: number) => new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 2 }).format(value);
const count = (value: number | null) => value === null ? '—' : new Intl.NumberFormat('ru-RU').format(value);

export function WbClusterTable({ rows }: { rows: WbClusterRow[] }) {
  const [page, setPage] = useState(0);
  const [rowsPerPage, setRowsPerPage] = useState(25);
  useEffect(() => { setPage(0); }, [rows]);
  if (rows.length === 0) {
    return <Alert severity="info">Кластеры за выбранный период ещё не загружены. Запустите их сбор в разделе «Кабинеты WB» после статистики кампаний.</Alert>;
  }
  return <>
    <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
      Расход и заказы по поисковым кластерам WB. Выручка кластера и его ДРР недоступны в текущем API.
      Прочерк означает, что WB не вернул показатель.
    </Typography>
    <TableContainer sx={{ maxHeight: 650 }}>
      <Table stickyHeader size="small" aria-label="Статистика поисковых кластеров WB">
        <TableHead><TableRow>
          <TableCell>Артикул WB</TableCell><TableCell>Поисковый кластер</TableCell>
          <TableCell align="right">Расход, ₽</TableCell><TableCell align="right">Показы</TableCell>
          <TableCell align="right">Клики</TableCell><TableCell align="right">Заказы</TableCell>
          <TableCell align="right">CPC, ₽</TableCell>
          <TableCell>Оценка</TableCell>
        </TableRow></TableHead>
        <TableBody>{rows.slice(page * rowsPerPage, (page + 1) * rowsPerPage).map((row) => <TableRow key={`${row.nomenclatureId}:${row.clusterName}`} hover>
          <TableCell>{row.nomenclatureId}</TableCell><TableCell>{row.clusterName}</TableCell>
          <TableCell align="right">{money(row.spend)}</TableCell><TableCell align="right">{count(row.views)}</TableCell>
          <TableCell align="right">{count(row.clicks)}</TableCell><TableCell align="right">{count(row.orders)}</TableCell>
          <TableCell align="right">{row.cpc === null ? '—' : money(row.cpc)}</TableCell>
          <TableCell>{row.assessment}</TableCell>
        </TableRow>)}</TableBody>
      </Table>
    </TableContainer>
    <TablePagination component="div" count={rows.length} page={page} rowsPerPage={rowsPerPage}
      onPageChange={(_, nextPage) => setPage(nextPage)}
      onRowsPerPageChange={(event) => { setRowsPerPage(Number(event.target.value)); setPage(0); }}
      rowsPerPageOptions={[25, 50, 100]} labelRowsPerPage="Строк на странице"
      labelDisplayedRows={({ from, to, count: total }) => `${from}–${to} из ${total}`} />
  </>;
}
