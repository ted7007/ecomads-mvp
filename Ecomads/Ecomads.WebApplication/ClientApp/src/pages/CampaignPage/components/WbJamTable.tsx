import { Accordion, AccordionDetails, AccordionSummary, Alert, Box, Button, MenuItem, Select, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { appRoutes } from '../../../app/routes';
import type { WbJamResponse } from '../campaignApi';
import { PaginationBar } from '../../../shared/ui/PaginationBar';

const count = (value: number | null) => value === null ? '—' : new Intl.NumberFormat('ru-RU').format(value);
const position = (value: number | null) => value === null ? '—' : new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 1 }).format(value);
const shortDate = (date: string) => date.replace(/^(\d{4})-(\d{2})-(\d{2})$/, '$3.$2.$1');
const jamCell = { bgcolor: '#F7F0FF', color: '#6841A1' };

export function WbJamTable({ report, onSelectPeriod }: { report: WbJamResponse;
  onSelectPeriod: (startDate: string, endDate: string) => void }) {
  const [page, setPage] = useState(0);
  const [rowsPerPage, setRowsPerPage] = useState(5);
  useEffect(() => { setPage(0); }, [report.rows]);
  const chooser = report.availableReports.length ? <Select size="small" value={`${report.startDate}/${report.endDate}`}
    aria-label="Период отчёта Джема" onChange={(event) => {
      const [start, end] = event.target.value.split('/'); onSelectPeriod(start, end);
    }} sx={{ minWidth: 220 }}>
    {report.availableReports.map((period) => <MenuItem key={`${period.startDate}/${period.endDate}`}
      value={`${period.startDate}/${period.endDate}`}>
      {shortDate(period.startDate)}–{shortDate(period.endDate)}
    </MenuItem>)}
  </Select> : null;
  return <>
    <Box sx={{ mb: 1.5 }}>{chooser}</Box>
    {report.reportStatus === 'access_denied' ? <Alert severity="warning">Нет доступа к Джему. Проверьте права токена и подписку.</Alert> : null}
    {report.reportStatus === 'partial' ? <Alert severity="warning">Загрузка частичная: проверено {report.checkedArticleCount} из {report.articleCount} товаров.</Alert> : null}
    {report.reportStatus === 'legacy' ? <Alert severity="info">Старый отчёт сохранён без сведений о товарах с пустым ответом.</Alert> : null}
    {!report.rows.length ? <Alert severity="info" action={<Button size="small" component={Link} to={appRoutes.wbStores}>Проверить загрузку</Button>}>
      {report.reportStatus === 'empty' ? 'WB не вернул поисковые запросы по товарам этого отчёта.' :
        report.reportStatus === 'not_loaded' ? 'Отчёт Джема ещё не загружен.' :
        report.reportStatus === 'partial' ? 'В обработанной части отчёта запросов нет.' : 'Запросов Джема за этот период нет.'}
    </Alert> : <>
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
          <TableCell align="right" sx={jamCell}>Частотность · Джем</TableCell><TableCell align="right" sx={jamCell}>Позиция · Джем</TableCell>
          <TableCell align="right" sx={jamCell}>Переходы · Джем</TableCell><TableCell align="right" sx={jamCell}>Корзины · Джем</TableCell>
          <TableCell align="right" sx={jamCell}>Заказы · Джем</TableCell><TableCell>Рекламный кластер</TableCell>
        </TableRow></TableHead>
        <TableBody>{report.rows.slice(page * rowsPerPage, (page + 1) * rowsPerPage).map((row) =>
          <TableRow key={`${row.nomenclatureId}:${row.searchText}`} hover>
            <TableCell>{row.nomenclatureId}</TableCell><TableCell>{row.searchText}</TableCell>
            <TableCell align="right" sx={jamCell}>{count(row.frequency)}</TableCell>
            <TableCell align="right" sx={jamCell}>{position(row.averagePosition)}</TableCell>
            <TableCell align="right" sx={jamCell}>{count(row.openCard)}</TableCell>
            <TableCell align="right" sx={jamCell}>{count(row.addToCart)}</TableCell>
            <TableCell align="right" sx={jamCell}>{count(row.orders)}</TableCell>
            <TableCell>{row.matchingLoadedAdCluster ? 'Совпадает' : '—'}</TableCell>
          </TableRow>)}</TableBody>
      </Table>
    </TableContainer>
    <PaginationBar count={report.rows.length} page={page} rowsPerPage={rowsPerPage} onPageChange={setPage} onRowsPerPageChange={(size) => { setRowsPerPage(size); setPage(0); }} />
    </>}
  </>;
}
