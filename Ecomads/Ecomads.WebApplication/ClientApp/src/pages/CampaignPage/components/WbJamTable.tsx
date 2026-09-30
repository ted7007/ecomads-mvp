import { Accordion, AccordionDetails, AccordionSummary, Alert, Button, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { appRoutes } from '../../../app/routes';
import type { WbJamResponse } from '../campaignApi';
import { PaginationBar } from '../../../shared/ui/PaginationBar';

const count = (value: number | null) => value === null ? '—' : new Intl.NumberFormat('ru-RU').format(value);
const position = (value: number | null) => value === null ? '—' : new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 1 }).format(value);
const shortDate = (date: string) => date.replace(/^(\d{4})-(\d{2})-(\d{2})$/, '$3.$2.$1');

export function WbJamTable({ report }: { report: WbJamResponse }) {
  const [page, setPage] = useState(0);
  const [rowsPerPage, setRowsPerPage] = useState(5);
  useEffect(() => { setPage(0); }, [report.rows]);
  if (!report.rows.length) {
    return <Alert severity="info" action={<Button size="small" component={Link} to={appRoutes.wbStores}>Проверить загрузку</Button>}>
      Запросов Джема за выбранный период пока нет. {report.jamStatus === 'access_denied' ? 'WB отклонил последний запрос: проверьте права токена и доступ к отчёту.' : ''}
    </Alert>;
  }
  return <>
    <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
      За {shortDate(report.startDate)}–{shortDate(report.endDate)}: запросы есть по {report.articlesWithQueries} из {report.articleCount} товаров кампании.
    </Typography>
    <Accordion disableGutters elevation={0} sx={{ bgcolor: 'transparent', mb: 1, '&:before': { display: 'none' } }}>
      <AccordionSummary expandIcon="⌄"><Typography variant="body2" color="text.secondary">О данных Джема</Typography></AccordionSummary>
      <AccordionDetails><Typography variant="body2" color="text.secondary">
        WB возвращает до 30 запросов на товар, отобранных по заказам. Данные относятся к товарам в поиске, а не только к этой рекламной кампании.
        Позиция не подтверждена как органическая. Совпадение с рекламным кластером означает лишь совпадение текста.
      </Typography></AccordionDetails>
    </Accordion>
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
    <PaginationBar count={report.rows.length} page={page} rowsPerPage={rowsPerPage} onPageChange={setPage} onRowsPerPageChange={(size) => { setRowsPerPage(size); setPage(0); }} />
  </>;
}
