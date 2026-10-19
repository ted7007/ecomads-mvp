import { Accordion, AccordionDetails, AccordionSummary, Alert, Button, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Tooltip, Typography } from '@mui/material';
import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { appRoutes } from '../../../app/routes';
import type { WbClusterRow, WbJamRow } from '../campaignApi';
import { PaginationBar } from '../../../shared/ui/PaginationBar';
import type { ClusterRecommendationResponse } from '../../../shared/api/clusterRecommendationApi';
import { clusterAssessment } from './clusterAssessment';

const money = (value: number) => new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 2 }).format(value);
const count = (value: number | null) => value === null ? '—' : new Intl.NumberFormat('ru-RU').format(value);

const jamCell = { bgcolor: '#F7F0FF', color: '#6841A1' };
const estimateHint = 'Оценка по средней стоимости заказанных товаров этой пары «кампания + артикул» за выбранный период: сумма заказов fullstats / количество заказанных единиц (shks) × shks кластера. ДРР = расход кластера / оценочная сумма × 100%. Это не фактическая выручка кластера; данные вне выбранного периода не используются.';

export function WbClusterTable({ rows, jamRows = [], emptyText, report, assessmentLoading, assessmentError }: {
  rows: WbClusterRow[]; jamRows?: WbJamRow[]; emptyText?: string;
  report?: ClusterRecommendationResponse; assessmentLoading?: boolean; assessmentError?: boolean;
}) {
  const [page, setPage] = useState(0);
  const [rowsPerPage, setRowsPerPage] = useState(5);
  useEffect(() => { setPage(0); }, [rows]);
  const jamByCluster = new Map(jamRows.filter((row) => row.matchingLoadedAdCluster)
    .map((row) => [`${row.nomenclatureId}:${row.searchText.trim().toLocaleLowerCase('ru-RU')}`, row]));
  const evaluations = new Map((report?.enabled ? report.evaluations : [])
    .filter(row => row.subject.scope === 'cluster')
    .map(row => [JSON.stringify([row.subject.nomenclatureId, row.subject.clusterName]), row]));
  if (rows.length === 0) {
    return <Alert severity="info" action={<Button size="small" component={Link} to={appRoutes.wbStores}>Проверить загрузку</Button>}>
      {emptyText ?? 'Поисковые кластеры за выбранный период пока не загружены.'}
    </Alert>;
  }
  return <>
    <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
      Показатели таблицы — за выбранный период отчёта. Вывод и действие — за отдельные 14 дней
      {report?.enabled ? `: ${report.window.startDate.split('-').reverse().join('.')} — ${report.window.endDate.split('-').reverse().join('.')}, МСК` : ''}.
    </Typography>
    <Accordion disableGutters elevation={0} sx={{ bgcolor: 'transparent', mb: 1, '&:before': { display: 'none' } }}>
      <AccordionSummary expandIcon="⌄"><Typography variant="body2" color="text.secondary">О показателях кластеров</Typography></AccordionSummary>
      <AccordionDetails><Typography variant="body2" color="text.secondary">
        WB передаёт расход и заказы по поисковым кластерам, но не их фактическую выручку и ДРР. {estimateHint} Неполная загрузка отмечена отдельно; при отсутствии средней стоимости выводится прочерк. Фиолетовые столбцы — данные Джема по запросу товара при точном совпадении текста с кластером. Позиция не подтверждена как органическая.
      </Typography></AccordionDetails>
    </Accordion>
    <TableContainer sx={{ maxHeight: 650 }}>
      <Table stickyHeader size="small" aria-label="Статистика поисковых кластеров WB">
        <TableHead><TableRow>
          <TableCell>Артикул WB</TableCell><TableCell>Поисковый кластер</TableCell>
          <TableCell align="right">Расход, ₽</TableCell><TableCell align="right">Показы</TableCell>
          <TableCell align="right">Клики</TableCell><TableCell align="right">Корзины</TableCell><TableCell align="right">Заказы</TableCell>
          <TableCell align="right">Заказанные единицы</TableCell>
          <TableCell align="right">CPC, ₽</TableCell>
          <TableCell align="right"><Tooltip title={estimateHint}><span>Сумма заказов, ₽ · Оценка</span></Tooltip></TableCell>
          <TableCell align="right"><Tooltip title={estimateHint}><span>ДРР, % · Оценка</span></Tooltip></TableCell>
          <TableCell align="right" sx={jamCell}>Частотность · Джем</TableCell>
          <TableCell align="right" sx={jamCell}>Позиция · Джем</TableCell>
          <TableCell sx={{ minWidth: 240 }}>Вывод за 14 дней</TableCell>
          <TableCell sx={{ minWidth: 220 }}>Действие</TableCell>
        </TableRow></TableHead>
        <TableBody>{rows.slice(page * rowsPerPage, (page + 1) * rowsPerPage).map((row) => {
          const jam = jamByCluster.get(`${row.nomenclatureId}:${row.clusterName.trim().toLocaleLowerCase('ru-RU')}`);
          const evaluation = evaluations.get(JSON.stringify([row.nomenclatureId, row.clusterName]));
          const assessment = assessmentError ? { title: 'Не удалось загрузить оценку', reason: 'Повторите загрузку рекомендаций ниже.', color: 'text.secondary' }
            : assessmentLoading ? { title: 'Оценка загружается…', reason: '', color: 'text.secondary' }
            : report && !report.enabled ? { title: 'Оценка отключена', reason: report.message, color: 'text.secondary' }
            : clusterAssessment(evaluation);
          const triggered = evaluation?.rules.some(rule => rule.status === 'triggered');
          const presentation = !assessmentError && !assessmentLoading && triggered ? evaluation?.presentation : null;
          return <TableRow key={`${row.nomenclatureId}:${row.clusterName}`} hover>
          <TableCell>{row.nomenclatureId}</TableCell><TableCell>{row.clusterName}</TableCell>
          <TableCell align="right">{money(row.spend)}</TableCell><TableCell align="right">{count(row.views)}</TableCell>
          <TableCell align="right">{count(row.clicks)}</TableCell><TableCell align="right">{count(row.carts)}</TableCell>
          <TableCell align="right">{count(row.orders)}</TableCell><TableCell align="right">{count(row.orderedProducts)}</TableCell>
          <TableCell align="right">{row.cpc === null ? '—' : money(row.cpc)}</TableCell>
          <TableCell align="right"><Tooltip title={row.estimateReason ?? estimateHint}><span>
            {row.estimatedOrderSum === null ? '—' : money(row.estimatedOrderSum)}
          </span></Tooltip>{row.estimateStatus !== 'complete' ? <Typography variant="caption" display="block" color="warning.main">
            {row.estimateStatus === 'preliminary' ? 'Предварительно' : row.estimateReason ?? 'Средняя цена за выбранный период неизвестна'}
          </Typography> : null}</TableCell>
          <TableCell align="right"><Tooltip title={row.estimateReason ?? (row.estimatedOrderSum === 0 ? 'Оценочная сумма заказов равна нулю; ДРР не рассчитывается.' : estimateHint)}><span>
            {row.estimatedDrr === null ? '—' : money(row.estimatedDrr)}
          </span></Tooltip></TableCell>
          <TableCell align="right" sx={jamCell}>{count(jam?.frequency ?? null)}</TableCell>
          <TableCell align="right" sx={jamCell}>{jam?.averagePosition == null ? '—' : money(jam.averagePosition)}</TableCell>
          <TableCell>
            <Typography variant="body2" fontWeight={600} color={assessment.color}>{assessment.title}</Typography>
            <Typography variant="caption" color="text.secondary" display="block">{assessment.reason}</Typography>
          </TableCell>
          <TableCell>
            {presentation ? <>
              <Typography variant="body2">{presentation.primary.label}</Typography>
              {presentation.restrictions.map(restriction => <Typography key={restriction.code} variant="caption" display="block" color="text.secondary">
                {restriction.label}
              </Typography>)}
            </> : '—'}
          </TableCell>
        </TableRow>;})}</TableBody>
      </Table>
    </TableContainer>
    <PaginationBar count={rows.length} page={page} rowsPerPage={rowsPerPage} onPageChange={setPage} onRowsPerPageChange={(size) => { setRowsPerPage(size); setPage(0); }} />
  </>;
}
