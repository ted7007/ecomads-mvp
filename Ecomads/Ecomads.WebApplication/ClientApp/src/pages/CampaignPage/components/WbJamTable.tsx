import { Alert, Table, TableBody, TableCell, TableContainer, TableHead, TablePagination, TableRow, Typography } from '@mui/material';
import { useEffect, useState } from 'react';
import type { WbJamResponse } from '../campaignApi';

const count = (value: number | null) => value === null ? '—' : new Intl.NumberFormat('ru-RU').format(value);
const position = (value: number | null) => value === null ? '—' : new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 1 }).format(value);

export function WbJamTable({ report }: { report: WbJamResponse }) {
  const [page, setPage] = useState(0);
  const [rowsPerPage, setRowsPerPage] = useState(25);
  useEffect(() => { setPage(0); }, [report.rows]);
  if (!report.rows.length) {
    return <Alert severity="info">За выбранный период строк отчёта Джема пока нет. Проверьте состояние сбора в разделе «Кабинеты WB»; если его ещё не запускали, выберите период до 7 дней и нажмите «Загрузить Джем». {report.jamStatus === 'access_denied' ? 'Последний запрос WB отклонил из-за доступа.' : ''}</Alert>;
  }
  return <>
    <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
      Отчёт WB за {report.startDate} — {report.endDate}: данные есть по {report.articlesWithQueries} из {report.articleCount} товаров кампании.
      Позиция относится к товару в поисковой выдаче; этот API не помечает её как органическую. Совпадение с рекламным кластером означает лишь совпадение текста в загруженных данных.
    </Typography>
    <TableContainer sx={{ maxHeight: 650 }}>
      <Table stickyHeader size="small" aria-label="Поисковые запросы Джема">
        <TableHead><TableRow>
          <TableCell>Артикул WB</TableCell><TableCell>Поисковый запрос</TableCell>
          <TableCell align="right">Частотность</TableCell><TableCell align="right">Позиция</TableCell>
          <TableCell align="right">Переходы</TableCell><TableCell align="right">Корзины</TableCell>
          <TableCell align="right">Заказы</TableCell><TableCell>Рекламный кластер</TableCell>
        </TableRow></TableHead>
        <TableBody>{report.rows.slice(page * rowsPerPage, (page + 1) * rowsPerPage).map((row) =>
          <TableRow key={`${row.nomenclatureId}:${row.searchText}`} hover>
            <TableCell>{row.nomenclatureId}</TableCell><TableCell>{row.searchText}</TableCell>
            <TableCell align="right">{count(row.frequency)}</TableCell>
            <TableCell align="right">{position(row.averagePosition)}</TableCell>
            <TableCell align="right">{count(row.openCard)}</TableCell>
            <TableCell align="right">{count(row.addToCart)}</TableCell>
            <TableCell align="right">{count(row.orders)}</TableCell>
            <TableCell>{row.matchingLoadedAdCluster ? 'Совпадает' : '—'}</TableCell>
          </TableRow>)}</TableBody>
      </Table>
    </TableContainer>
    <TablePagination component="div" count={report.rows.length} page={page} rowsPerPage={rowsPerPage}
      onPageChange={(_, nextPage) => setPage(nextPage)}
      onRowsPerPageChange={(event) => { setRowsPerPage(Number(event.target.value)); setPage(0); }}
      rowsPerPageOptions={[25, 50, 100]} labelRowsPerPage="Строк на странице"
      labelDisplayedRows={({ from, to, count: total }) => `${from}–${to} из ${total}`} />
  </>;
}
