import { Chip, Stack, Tooltip, Typography } from '@mui/material';
import type { WbClusterRow, WbJamRow } from '../campaignApi';
import type { ClusterRecommendationResponse } from '../../../shared/api/clusterRecommendationApi';
import { clusterAssessment } from './clusterAssessment';
import { lightUiTokens } from '../../../app/lightUiTokens';
import { formatMoney as money } from '../../../shared/lib/formatMoney';
import { formatPercent as percent } from '../../../shared/lib/formatPercent';
import { EmptyState } from '../../../shared/ui/EmptyState';
import { ReportTable, type ReportColumn } from './ReportTable';
import { clusterCtr, pairKey } from './campaignReportModel';
const count = (v: number | null | undefined) => v == null ? '—' : v.toLocaleString('ru-RU', { maximumFractionDigits: 1 });
export function WbClusterTable({ rows, jamRows = [], report, assessmentLoading, assessmentError, resetKey }: {
  rows: WbClusterRow[]; jamRows?: WbJamRow[]; report?: ClusterRecommendationResponse; assessmentLoading?: boolean; assessmentError?: boolean; resetKey?: string;
}) {
  const jam = new Map(jamRows.filter(r => r.matchingLoadedAdCluster).map(r => [pairKey(r.nomenclatureId, r.searchText), r]));
  const evaluations = new Map((report?.enabled ? report.evaluations : []).filter(r => r.subject.scope === 'cluster').map(r => [pairKey(r.subject.nomenclatureId ?? '', r.subject.clusterName ?? ''), r]));
  const data = rows.map(row => {
    const evaluation = evaluations.get(pairKey(row.nomenclatureId, row.clusterName));
    return {
      ...row, jam: jam.get(pairKey(row.nomenclatureId, row.clusterName)), evaluation,
      assessment: assessmentError ? { title: 'Оценка недоступна', reason: 'Повторите загрузку рекомендаций', color: 'text.secondary' } : assessmentLoading ? { title: 'Оценка загружается', reason: '', color: 'text.secondary' } : report && !report.enabled ? { title: 'Оценка отключена', reason: report.message, color: 'text.secondary' } : clusterAssessment(evaluation)
    };
  });
  type Row = typeof data[number];
  const name = (r: Row) => <><Typography sx={{overflowWrap: 'anywhere'}} fontWeight={700} variant="body2">{r.clusterName}</Typography><Typography variant="caption" color="text.secondary">{r.nomenclatureId}</Typography></>;
  const assessment = (r: Row) => { const launch = r.assessment.title.toLowerCase().includes('запуск'); const problem = r.assessment.color === 'warning.main'; return <Tooltip title={`${r.assessment.title}. ${r.assessment.reason}`}><Chip size="small" label={problem ? 'Есть проблема' : launch ? 'Запуск' : r.assessment.title} sx={{ maxWidth: '100%', height: 'auto', py: .5, '& .MuiChip-label': { whiteSpace: 'normal' }, ...(problem ? lightUiTokens.status.warning : launch ? lightUiTokens.status.info : lightUiTokens.status.neutral) }} /></Tooltip>; };
  const columns: ReportColumn<Row>[] = [
    { key: 'name', label: 'Кластер / артикул', text: true, width: '18%', value: r => r.clusterName, render: name },
    { key: 'views', label: 'Показы', value: r => r.views, render: r => count(r.views) },
    { key: 'clicks', label: 'Клики', value: r => r.clicks, render: r => count(r.clicks) },
    { key: 'ctr', label: 'CTR, %', value: clusterCtr, render: r => percent(clusterCtr(r), 1) },
    { key: 'spend', label: 'Расход, ₽', value: r => r.spend, render: r => money(r.spend) },
    { key: 'orders', width: '9%', label: 'Сумма заказов, ₽ · оценка', value: r => r.estimatedOrderSum, render: r => <Tooltip title={r.estimateReason ?? 'Оценка по средней цене единицы товара; это не фактическая выручка кластера'}><span>{money(r.estimatedOrderSum)}</span></Tooltip> },
    { key: 'drr', label: 'ДРР, % · оценка', value: r => r.estimatedDrr, render: r => percent(r.estimatedDrr, 1) },
    { key: 'position', width: '9%', label: 'Позиция рекламы', value: r => r.averagePosition, render: r => <Tooltip title={r.positionReason ?? 'Среднее известных дневных позиций WB за выбранный период'}><span>{count(r.averagePosition)}</span></Tooltip> },
    { key: 'frequency', width: '10%', label: 'Частотность · Джем', value: r => r.jam?.frequency, render: r => count(r.jam?.frequency), sx: lightUiTokens.jamCell },
    { key: 'jamPosition', width: '8%', label: 'Позиция · Джем', value: r => r.jam?.averagePosition, render: r => count(r.jam?.averagePosition), sx: lightUiTokens.jamCell },
    { key: 'assessment', label: 'Оценка · 14 дней', text: true, width: '14%', value: r => r.assessment.title, render: assessment }];
  if (!rows.length) return <EmptyState title="Кластеры за выбранный период не загружены" />;
  return <><Typography variant="caption" color="text.secondary">Показатели — за выбранные даты. Оценка — за 14 дней{report ? ` (${report.window.startDate} — ${report.window.endDate})` : ''}.</Typography>
    <ReportTable rows={data} columns={columns} rowId={r => pairKey(r.nomenclatureId, r.clusterName)} label="Поисковые кластеры" resetKey={resetKey}
      mobile={r => <Stack spacing={.75}>{name(r)}{assessment(r)}<Typography>Показы: {count(r.views)} · Клики: {count(r.clicks)} · CTR: {percent(clusterCtr(r), 1)}</Typography><Typography>Расход: {money(r.spend)} · Заказы: {count(r.orders)} · ДРР: {percent(r.estimatedDrr, 1)}</Typography><Typography>Сумма заказов · оценка: {money(r.estimatedOrderSum)}</Typography><Typography color="secondary.main">Джем: частотность {count(r.jam?.frequency)} · позиция {count(r.jam?.averagePosition)}</Typography><Typography variant="caption">Позиция рекламы: {count(r.averagePosition)}</Typography></Stack>}
      details={r => <Stack spacing={1}>{name(r)}<Typography>{r.assessment.title}</Typography><Typography>{r.assessment.reason}</Typography><Typography variant="caption">Оценка за 14 дней: {report?.window.startDate} — {report?.window.endDate}</Typography><Typography>{r.evaluation?.presentation?.primary.label}</Typography><Typography>Корзины: {count(r.carts)} · Заказы: {count(r.orders)} · Единицы: {count(r.orderedProducts)} · CPC: {money(r.cpc)}</Typography><Typography>Средняя цена: {money(r.averageOrderedUnitPrice)}</Typography><Typography>Оценочная сумма: {money(r.estimatedOrderSum)} · ДРР: {percent(r.estimatedDrr, 1)}</Typography><Typography>{r.estimateReason ?? 'Оценка по средней стоимости единицы товара за выбранный период.'}</Typography><Typography>Позиция рекламы: {count(r.averagePosition)} · дней с позицией: {r.positionDays}</Typography><Typography>{r.positionReason}</Typography><Typography>Качество оценки: {{ complete: 'полная', preliminary: 'предварительная', unavailable: 'недоступна' }[r.estimateStatus]}</Typography>{r.evaluation?.presentation?.restrictions.map(x => <Typography key={x.code}>{x.label}</Typography>)}</Stack>} /></>;
}
