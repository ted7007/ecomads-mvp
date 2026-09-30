import { Accordion, AccordionDetails, AccordionSummary, Alert, Button, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { appRoutes } from '../../../app/routes';
import type { WbClusterRow, WbJamRow } from '../campaignApi';
import { PaginationBar } from '../../../shared/ui/PaginationBar';

const money = (value: number) => new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 2 }).format(value);
const count = (value: number | null) => value === null ? '—' : new Intl.NumberFormat('ru-RU').format(value);

const jamCell = { bgcolor: '#F7F0FF', color: '#6841A1' };

export function WbClusterTable({ rows, jamRows = [] }: { rows: WbClusterRow[]; jamRows?: WbJamRow[] }) {
  const [page, setPage] = useState(0);
  const [rowsPerPage, setRowsPerPage] = useState(5);
  useEffect(() => { setPage(0); }, [rows]);
  const jamByCluster = new Map(jamRows.filter((row) => row.matchingLoadedAdCluster)
    .map((row) => [`${row.nomenclatureId}:${row.searchText.trim().toLocaleLowerCase('ru-RU')}`, row]));
  if (rows.length === 0) {
    return <Alert severity="info" action={<Button size="small" component={Link} to={appRoutes.wbStores}>Проверить загрузку</Button>}>
      Поисковые кластеры за выбранный период пока не загружены.
    </Alert>;
  }
  return <>
    <Accordion disableGutters elevation={0} sx={{ bgcolor: 'transparent', mb: 1, '&:before': { display: 'none' } }}>
      <AccordionSummary expandIcon="⌄"><Typography variant="body2" color="text.secondary">О показателях кластеров</Typography></AccordionSummary>
      <AccordionDetails><Typography variant="body2" color="text.secondary">
        WB передаёт расход и заказы по поисковым кластерам, но не их выручку и ДРР. Фиолетовые столбцы — данные Джема по запросу товара при точном совпадении текста с кластером. Позиция не подтверждена как органическая. Прочерк означает отсутствие подтверждённого совпадения или значения.
      </Typography></AccordionDetails>
    </Accordion>
    <TableContainer sx={{ maxHeight: 650 }}>
      <Table stickyHeader size="small" aria-label="Статистика поисковых кластеров WB">
        <TableHead><TableRow>
          <TableCell>Артикул WB</TableCell><TableCell>Поисковый кластер</TableCell>
          <TableCell align="right">Расход, ₽</TableCell><TableCell align="right">Показы</TableCell>
          <TableCell align="right">Клики</TableCell><TableCell align="right">Заказы</TableCell>
          <TableCell align="right">CPC, ₽</TableCell>
          <TableCell align="right" sx={jamCell}>Частотность · Джем</TableCell>
          <TableCell align="right" sx={jamCell}>Позиция · Джем</TableCell>
          <TableCell>Оценка</TableCell>
        </TableRow></TableHead>
        <TableBody>{rows.slice(page * rowsPerPage, (page + 1) * rowsPerPage).map((row) => {
          const jam = jamByCluster.get(`${row.nomenclatureId}:${row.clusterName.trim().toLocaleLowerCase('ru-RU')}`);
          return <TableRow key={`${row.nomenclatureId}:${row.clusterName}`} hover>
          <TableCell>{row.nomenclatureId}</TableCell><TableCell>{row.clusterName}</TableCell>
          <TableCell align="right">{money(row.spend)}</TableCell><TableCell align="right">{count(row.views)}</TableCell>
          <TableCell align="right">{count(row.clicks)}</TableCell><TableCell align="right">{count(row.orders)}</TableCell>
          <TableCell align="right">{row.cpc === null ? '—' : money(row.cpc)}</TableCell>
          <TableCell align="right" sx={jamCell}>{count(jam?.frequency ?? null)}</TableCell>
          <TableCell align="right" sx={jamCell}>{jam?.averagePosition == null ? '—' : money(jam.averagePosition)}</TableCell>
          <TableCell>{row.assessment}</TableCell>
        </TableRow>;})}</TableBody>
      </Table>
    </TableContainer>
    <PaginationBar count={rows.length} page={page} rowsPerPage={rowsPerPage} onPageChange={setPage} onRowsPerPageChange={(size) => { setRowsPerPage(size); setPage(0); }} />
  </>;
}
