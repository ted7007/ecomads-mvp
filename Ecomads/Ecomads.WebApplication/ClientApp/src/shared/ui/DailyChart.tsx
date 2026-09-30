import { Box, Checkbox, FormControlLabel, Stack, Typography, useMediaQuery } from '@mui/material';
import { useState } from 'react';
import type { DailyPoint } from '../../pages/DashboardPage/dashboardApi';
import { formatMoney } from '../lib/formatMoney';
import { formatPercent } from '../lib/formatPercent';

export type MetricKey = 'revenue' | 'spend' | 'drr' | 'ctr' | 'clicks' | 'orders' | 'cpc';
export const metricColors: Record<MetricKey, string> = {
  revenue: '#A55EEA', spend: '#F08A00', drr: '#14A394', ctr: '#E0457B',
  clicks: '#1E7BF2', orders: '#1E7BF2', cpc: '#8A94A3'
};
const labels: Record<MetricKey, string> = {
  revenue: 'Заказы с рекламы', spend: 'Расход', drr: 'ДРР рекламы', ctr: 'CTR',
  clicks: 'Клики', orders: 'Заказы с рекламы, шт.', cpc: 'CPC'
};
const moneyMetrics: MetricKey[] = ['revenue', 'spend', 'cpc'];
const formatValue = (metric: MetricKey, value: number) => moneyMetrics.includes(metric) ? formatMoney(value) :
  metric === 'drr' || metric === 'ctr' ? formatPercent(value, metric === 'drr' ? 1 : 2) : value.toLocaleString('ru-RU');
const read = (day: DailyPoint, metric: MetricKey): number | null =>
  metric === 'cpc' ? day.spend !== null && day.clicks ? day.spend / day.clicks : null : day[metric];
const shortDate = (date: string) => date.slice(8, 10) + '.' + date.slice(5, 7);

function paths(values: (number | null)[], x: (index: number) => number, y: (value: number) => number) {
  const result: string[] = [];
  let points: [number, number][] = [];
  const flush = () => {
    if (!points.length) return;
    let path = `M${points[0][0].toFixed(1)} ${points[0][1].toFixed(1)}`;
    for (let i = 0; i < points.length - 1; i++) {
      const a = points[i - 1] ?? points[i], b = points[i], c = points[i + 1], d = points[i + 2] ?? c;
      const dx = (c[0] - b[0]) / 3;
      path += ` C${(b[0] + dx).toFixed(1)} ${(b[1] + (c[1] - a[1]) / 6).toFixed(1)}` +
        ` ${(c[0] - dx).toFixed(1)} ${(c[1] - (d[1] - b[1]) / 6).toFixed(1)} ${c[0].toFixed(1)} ${c[1].toFixed(1)}`;
    }
    result.push(path); points = [];
  };
  values.forEach((value, index) => { if (value === null) flush(); else points.push([x(index), y(value)]); });
  flush();
  return result;
}

export function DailyChart({ days, title = 'Динамика показателей', selectedMetrics, previousDays = [] }: {
  days: DailyPoint[]; title?: string; selectedMetrics: MetricKey[]; previousDays?: DailyPoint[];
}) {
  const [showPrevious, setShowPrevious] = useState(true);
  const [hovered, setHovered] = useState<number | null>(null);
  const compact = useMediaQuery('(max-width:600px)');
  const left = compact ? 43 : 65, right = compact ? 337 : 1015, top = 18, bottom = 207;
  const x = (index: number) => left + (days.length <= 1 ? .5 : index / (days.length - 1)) * (right - left);
  const allLoaded = (series: DailyPoint[], metric: MetricKey) => series.every((day) =>
    day.expectedCampaigns > 0 && day.loadedCampaigns >= day.expectedCampaigns && read(day, metric) !== null);
  const series = selectedMetrics.map((metric) => {
    const current = days.map((day) => read(day, metric));
    const comparable = days.length > 0 && days.length === previousDays.length &&
      allLoaded(days, metric) && allLoaded(previousDays, metric);
    const previous = comparable && showPrevious ? previousDays.map((day) => read(day, metric)) : [];
    const values = [...current, ...previous].filter((value): value is number => value !== null);
    const min = values.length ? Math.min(...values) : 0, max = values.length ? Math.max(...values) : 1;
    const spread = max - min || Math.max(Math.abs(max) * .2, 1);
    const lo = Math.max(0, min - spread * .15), hi = max + spread * .12;
    const y = (value: number) => bottom - (value - lo) / (hi - lo) * (bottom - top);
    return { metric, current, previous, comparable, lo, hi, currentPaths: paths(current, x, y), previousPaths: paths(previous, x, y) };
  });
  const active = hovered !== null && hovered >= 0 && hovered < days.length ? hovered : null;
  const axis = (item: typeof series[number], side: 'left' | 'right') => [0, 1, 2, 3].map((step) => {
    const value = item.hi - step * (item.hi - item.lo) / 3;
    const label = moneyMetrics.includes(item.metric) && Math.abs(value) >= 10000 ?
      `${new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 0 }).format(value / 1000)} тыс.` :
      new Intl.NumberFormat('ru-RU', { maximumFractionDigits: Math.abs(value) < 10 ? 1 : 0 }).format(value) +
      (item.metric === 'drr' || item.metric === 'ctr' ? '%' : '');
    return <text key={step} x={side === 'left' ? left - 8 : right + 8}
      y={top + step * (bottom - top) / 3 + 4} textAnchor={side === 'left' ? 'end' : 'start'}
      fontSize="11" fill={metricColors[item.metric]}>{label}</text>;
  });

  return <Box>
    <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ sm: 'center' }} gap={1} sx={{ mb: 1 }}>
      <Box>
        <Typography variant="h6" fontWeight={800}>{title}</Typography>
        <Typography variant="body2" color="text.secondary">Выбирайте линии карточками KPI · у каждого показателя своя шкала · пропуски не считаются нулём</Typography>
      </Box>
      <FormControlLabel sx={{ m: 0, whiteSpace: 'nowrap' }} control={<Checkbox size="small" checked={showPrevious}
        onChange={(_, checked) => setShowPrevious(checked)} />} label={<Typography variant="body2">Предыдущий период</Typography>} />
    </Stack>
    <Box sx={{ position: 'relative', width: '100%' }} onMouseLeave={() => setHovered(null)}>
      <svg role="img" aria-label={`График по дням: ${selectedMetrics.map((metric) => labels[metric]).join(', ')}`}
        viewBox={`0 0 ${compact ? 380 : 1080} 255`} width="100%" style={{ display: 'block' }}>
        {[0, 1, 2, 3].map((step) => <line key={step} x1={left} x2={right}
          y1={top + step * (bottom - top) / 3} y2={top + step * (bottom - top) / 3}
          stroke={step === 3 ? '#D6DBE5' : '#E6E9F0'} />)}
        {series[0] ? axis(series[0], 'left') : null}
        {!compact && series[1] ? axis(series[1], 'right') : null}
        {active !== null && selectedMetrics.length ? <line x1={x(active)} x2={x(active)} y1={top} y2={bottom} stroke="#A2A7B3" /> : null}
        {series.map((item) => <g key={item.metric}>
          {item.previousPaths.map((path, index) => <path key={`p${index}`} d={path} fill="none"
            stroke={metricColors[item.metric]} strokeOpacity=".4" strokeWidth="2" strokeDasharray="5 5" />)}
          {item.currentPaths.map((path, index) => <path key={index} d={path} fill="none"
            stroke={metricColors[item.metric]} strokeWidth="2.5" strokeLinecap="round" />)}
        </g>)}
        {days.map((day, index) => <rect key={day.date}
          x={index === 0 ? left : (x(index - 1) + x(index)) / 2} y={top}
          width={index === days.length - 1 ? right - x(index) + 1 : (x(index + 1) - (index === 0 ? left : x(index - 1))) / 2}
          height={bottom - top} fill="transparent" onMouseEnter={() => setHovered(index)} onClick={() => setHovered(index)} />)}
        {days.map((day, index) => {
          const step = days.length <= 7 ? 1 : days.length <= 14 ? 2 : Math.ceil(days.length / 8);
          return index % step !== 0 && index !== days.length - 1 ? null :
            <text key={day.date} x={x(index)} y="235" textAnchor={index === 0 ? 'start' : index === days.length - 1 ? 'end' : 'middle'}
              fontSize="11" fill="#777985">{shortDate(day.date)}</text>;
        })}
      </svg>
      {active !== null && selectedMetrics.length ? <Box sx={{ position: 'absolute', top: 8, right: compact ? 5 : 20,
        maxWidth: 'min(280px, 75%)', bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider',
        borderRadius: 2, boxShadow: 3, p: 1.3, pointerEvents: 'none', zIndex: 1 }}>
        <Typography variant="body2" fontWeight={700} sx={{ mb: .5 }}>{shortDate(days[active].date)}
          {showPrevious && previousDays[active] ? ` / ${shortDate(previousDays[active].date)}` : ''}</Typography>
        {series.map((item) => <Typography key={item.metric} variant="body2" sx={{ color: metricColors[item.metric] }}>
          {labels[item.metric]}: {item.current[active] === null ? 'нет данных' : formatValue(item.metric, item.current[active]!)}
          {item.previous.length && item.previous[active] !== null ? ` / ${formatValue(item.metric, item.previous[active]!)}` : ''}
        </Typography>)}
      </Box> : null}
    </Box>
    {!selectedMetrics.length ? <Typography color="text.secondary" textAlign="center">Выберите показатель в карточках выше.</Typography> :
      !series.some((item) => item.current.some((value) => value !== null)) ?
        <Typography color="text.secondary" textAlign="center">За этот период нет загруженных значений выбранных показателей.</Typography> : null}
    {selectedMetrics.length ? <Stack direction="row" gap={2} flexWrap="wrap" sx={{ mt: .5 }}>
      {selectedMetrics.map((metric) => <Stack key={metric} direction="row" alignItems="center" gap={.7}>
        <Box sx={{ width: 15, height: 3, bgcolor: metricColors[metric], borderRadius: 2 }} />
        <Typography variant="caption">{labels[metric]}</Typography>
      </Stack>)}
    </Stack> : null}
    <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
      {showPrevious ? 'Пунктир показывается только при полном и сопоставимом предыдущем периоде. В подсказке: выбранный / предыдущий.' :
        'Сравнение с предыдущим периодом скрыто.'}
    </Typography>
  </Box>;
}
