import { Box, Chip, Stack, ToggleButton, ToggleButtonGroup, Typography, useMediaQuery } from '@mui/material';
import { useState } from 'react';
import type { DailyPoint } from '../../pages/DashboardPage/dashboardApi';
import { formatMoney } from '../lib/formatMoney';
import { formatPercent } from '../lib/formatPercent';

const metrics = [
  { key: 'spend', label: 'Расход', format: formatMoney },
  { key: 'revenue', label: 'Заказы с рекламы', format: formatMoney },
  { key: 'orders', label: 'Шт. заказов', format: (value: number) => value.toLocaleString('ru-RU') },
  { key: 'clicks', label: 'Клики', format: (value: number) => value.toLocaleString('ru-RU') },
  { key: 'drr', label: 'ДРР рекламы', format: (value: number) => formatPercent(value, 1) },
  { key: 'ctr', label: 'CTR', format: (value: number) => formatPercent(value, 2) }
] as const;
type MetricKey = typeof metrics[number]['key'];

export function DailyChart({ days, title = 'Динамика показателей' }: { days: DailyPoint[]; title?: string }) {
  const [metric, setMetric] = useState<MetricKey>('spend');
  const [hovered, setHovered] = useState<number | null>(null);
  const compact = useMediaQuery('(max-width:600px)');
  const selected = metrics.find((item) => item.key === metric)!;
  const values = days.map((day) => day[metric]);
  const available = values.filter((value): value is number => value !== null);
  const maximum = Math.max(1, ...available);
  const minimum = Math.min(0, ...available);
  const left = compact ? 40 : 55; const right = compact ? 325 : 880; const top = 28; const bottom = 228;
  const x = (index: number) => left + (days.length <= 1 ? 0.5 : index / (days.length - 1)) * (right - left);
  const y = (value: number) => bottom - (value - minimum) / (maximum - minimum || 1) * (bottom - top);
  const segments: string[] = [];
  let segment = '';
  values.forEach((value, index) => {
    if (value === null) { if (segment) segments.push(segment); segment = ''; return; }
    segment += `${segment ? ' L' : 'M'}${x(index).toFixed(1)},${y(value).toFixed(1)}`;
  });
  if (segment) segments.push(segment);
  const activeIndex = hovered !== null && values[hovered] !== null ? hovered : null;

  return <Box>
    <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" gap={1.5} sx={{ mb: 2 }}>
      <Box>
        <Typography variant="h6" fontWeight={800}>{title}</Typography>
        <Typography variant="body2" color="text.secondary">Выберите показатель · пропуски означают незагруженные дни</Typography>
      </Box>
      <Chip size="small" variant="outlined" label={`${days.filter((day) => day.spend !== null).length} из ${days.length} дней`} sx={{ alignSelf: 'flex-start' }} />
    </Stack>
    <ToggleButtonGroup exclusive value={metric} size="small" onChange={(_, value: MetricKey | null) => { if (value) setMetric(value); }}
      sx={{ display: 'flex', flexWrap: 'wrap', gap: .7, mb: 2, '& .MuiToggleButton-root': { border: '1px solid rgba(30,30,60,.12)!important', borderRadius: '10px!important', textTransform: 'none', px: 1.5 } }}>
      {metrics.map((item) => <ToggleButton key={item.key} value={item.key}>{item.label}</ToggleButton>)}
    </ToggleButtonGroup>
    {available.length === 0 ? <Typography color="text.secondary" sx={{ py: 7, textAlign: 'center' }}>За этот период ещё нет загруженных данных.</Typography> : <>
      <Box sx={{ width: '100%', minWidth: 0 }}>
        <svg role="img" aria-label={`График: ${selected.label}`} viewBox={`0 0 ${compact ? 340 : 920} 275`} width="100%" style={{ display: 'block', overflow: 'visible' }}>
          {[0, 1, 2, 3, 4].map((step) => {
            const yy = top + step * (bottom - top) / 4;
            const tick = maximum - step * (maximum - minimum) / 4;
            return <g key={step}>
              <line x1={left} x2={right} y1={yy} y2={yy} stroke="#DDE2ED" strokeDasharray="4 5" />
              <text x={left - 10} y={yy + 4} textAnchor="end" fontSize="11" fill="#777985">{tick >= 1000 ? `${(tick / 1000).toFixed(0)}к` : tick.toFixed(tick < 10 ? 1 : 0)}</text>
            </g>;
          })}
          {segments.map((path, index) => <path key={index} d={path} fill="none" stroke="#007AFF" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" />)}
          {values.map((value, index) => value === null ? null :
            <circle key={days[index].date} cx={x(index)} cy={y(value)} r={activeIndex === index ? 7 : 4}
              fill="#007AFF" stroke="white" strokeWidth="2" onMouseEnter={() => setHovered(index)} onMouseLeave={() => setHovered(null)}>
              <title>{days[index].date}: {selected.format(value)}</title>
            </circle>)}
          {[0, Math.floor((days.length - 1) / 2), days.length - 1].filter((item, index, array) => array.indexOf(item) === index).map((index) =>
            <text key={index} x={x(index)} y="257" textAnchor={index === 0 ? 'start' : index === days.length - 1 ? 'end' : 'middle'} fontSize="12" fill="#777985">
              {new Date(`${days[index].date}T00:00:00Z`).toLocaleDateString('ru-RU', { day: 'numeric', month: 'short', timeZone: 'UTC' })}
            </text>)}
        </svg>
      </Box>
      <Typography variant="body2" color="text.secondary" sx={{ mt: 1, minHeight: 21 }}>
        {activeIndex !== null ? `${days[activeIndex].date}: ${selected.format(values[activeIndex]!)} · загружено кампаний ${days[activeIndex].loadedCampaigns}/${days[activeIndex].expectedCampaigns}` :
          'Наведите на точку графика, чтобы увидеть значение и охват кампаний.'}
      </Typography>
    </>}
  </Box>;
}
