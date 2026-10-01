import { Box, Checkbox, FormControlLabel, Stack, Typography, useMediaQuery } from '@mui/material';
import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import type { KeyboardEvent, PointerEvent } from 'react';
import type { DailyPoint } from '../../pages/DashboardPage/dashboardApi';
import { formatMoney } from '../lib/formatMoney';
import { formatPercent } from '../lib/formatPercent';

export type MetricKey = 'revenue' | 'spend' | 'drr' | 'ctr' | 'clicks' | 'orders' | 'cpc';
export const metricColors: Record<MetricKey, string> = {
  revenue: '#A55EEA', spend: '#F08A00', drr: '#14A394', ctr: '#E0457B',
  clicks: '#1E7BF2', orders: '#1E7BF2', cpc: '#8A94A3'
};
const labels: Record<MetricKey, string> = {
  revenue: 'Заказы с рекламы', spend: 'Расход', drr: 'ДРР от рекламы', ctr: 'CTR',
  clicks: 'Клики', orders: 'Заказы с рекламы, шт.', cpc: 'CPC'
};
const moneyMetrics: MetricKey[] = ['revenue', 'spend', 'cpc'];
const formatValue = (metric: MetricKey, value: number) => metric === 'cpc' ? formatMoney(value, 1) :
  moneyMetrics.includes(metric) ? formatMoney(value) :
  metric === 'drr' || metric === 'ctr' ? formatPercent(value, 1) : value.toLocaleString('ru-RU');
const read = (day: DailyPoint, metric: MetricKey): number | null =>
  metric === 'cpc' ? day.spend !== null && day.clicks ? day.spend / day.clicks : null : day[metric];
const shortDate = (date: string) => date.slice(8, 10) + '.' + date.slice(5, 7);
const weekdays = ['вс', 'пн', 'вт', 'ср', 'чт', 'пт', 'сб'];
const dayLabel = (date: string) => `${weekdays[new Date(`${date}T00:00:00Z`).getUTCDay()]} ${shortDate(date)}`;
const rangeLabel = (series: DailyPoint[]) => series.length ? `${shortDate(series[0].date)} – ${shortDate(series[series.length - 1].date)}` : '';
const clamp = (value: number, min: number, max: number) => Math.max(min, Math.min(max, value));

function paths(values: (number | null)[], x: (index: number) => number, y: (value: number) => number) {
  const result: string[] = [];
  let points: [number, number][] = [];
  const flush = () => {
    if (!points.length) return;
    let path = `M${points[0][0].toFixed(1)} ${points[0][1].toFixed(1)}`;
    for (let i = 0; i < points.length - 1; i++) {
      const a = points[i - 1] ?? points[i], b = points[i], c = points[i + 1], d = points[i + 2] ?? c;
      const dx = (c[0] - b[0]) / 3;
      const bound = (value: number) => Math.max(Math.min(b[1], c[1]), Math.min(Math.max(b[1], c[1]), value));
      path += ` C${(b[0] + dx).toFixed(1)} ${bound(b[1] + (c[1] - a[1]) / 6).toFixed(1)}` +
        ` ${(c[0] - dx).toFixed(1)} ${bound(c[1] - (d[1] - b[1]) / 6).toFixed(1)} ${c[0].toFixed(1)} ${c[1].toFixed(1)}`;
    }
    result.push(path); points = [];
  };
  values.forEach((value, index) => { if (value === null) flush(); else points.push([x(index), y(value)]); });
  flush();
  return result;
}

type Hover = { index: number; x: number; y: number };

export function DailyChart({ days, title, selectedMetrics, previousDays = [] }: {
  days: DailyPoint[]; title?: string; selectedMetrics: MetricKey[]; previousDays?: DailyPoint[];
}) {
  const [showPrevious, setShowPrevious] = useState(true);
  const [hover, setHover] = useState<Hover | null>(null);
  const rootRef = useRef<HTMLDivElement>(null);
  const plotRef = useRef<HTMLDivElement>(null);
  const tipRef = useRef<HTMLDivElement>(null);
  const compact = useMediaQuery('(max-width:600px)');
  const width = compact ? 380 : 1080, height = compact ? 190 : 226;
  const left = compact ? 58 : 64, right = compact ? 372 : 1016, top = 10, bottom = compact ? 160 : 199, labelY = compact ? 180 : 218;
  const x = (index: number) => left + (days.length <= 1 ? .5 : index / (days.length - 1)) * (right - left);

  const series = useMemo(() => {
    const allLoaded = (list: DailyPoint[], metric: MetricKey) => list.every((day) =>
      day.expectedCampaigns > 0 && day.loadedCampaigns >= day.expectedCampaigns && read(day, metric) !== null);
    const xAt = (index: number) => left + (days.length <= 1 ? .5 : index / (days.length - 1)) * (right - left);
    return selectedMetrics.map((metric) => {
      const current = days.map((day) => read(day, metric));
      const comparable = days.length > 0 && days.length === previousDays.length &&
        allLoaded(days, metric) && allLoaded(previousDays, metric);
      const previous = comparable && showPrevious ? previousDays.map((day) => read(day, metric)) : [];
      const values = [...current, ...previous].filter((value): value is number => value !== null);
      const min = values.length ? Math.min(...values) : 0, max = values.length ? Math.max(...values) : 1;
      const spread = max - min || Math.max(Math.abs(max) * .2, 1);
      const lo = Math.max(0, min - spread * .15), hi = max + spread * .12;
      const y = (value: number) => bottom - (value - lo) / (hi - lo) * (bottom - top);
      return { metric, current, previous, comparable, lo, hi, currentPaths: paths(current, xAt, y), previousPaths: paths(previous, xAt, y) };
    });
  }, [days, previousDays, selectedMetrics, showPrevious, left, right, bottom]);

  const active = hover && hover.index < days.length && selectedMetrics.length ? hover.index : null;
  const hasPrevious = series.some((item) => item.previous.length > 0);
  const hasGaps = series.some((item) => item.current.some((value) => value === null) && item.current.some((value) => value !== null));
  const hiddenPrevious = showPrevious && previousDays.length > 0 && series.some((item) => !item.comparable);

  const indexAt = (plotX: number, plotWidth: number) => days.length <= 1 ? 0 :
    clamp(Math.round(((plotX * width / plotWidth) - left) / (right - left) * (days.length - 1)), 0, days.length - 1);
  const track = (event: PointerEvent<HTMLDivElement>) => {
    const rect = event.currentTarget.getBoundingClientRect();
    if (!days.length || !rect.width) return;
    const px = event.clientX - rect.left, py = event.clientY - rect.top;
    setHover({ index: indexAt(px, rect.width), x: px, y: py });
  };
  const step = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') { setHover(null); return; }
    if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight' || !days.length) return;
    event.preventDefault();
    const rect = event.currentTarget.getBoundingClientRect();
    const index = hover === null ? days.length - 1 : clamp(hover.index + (event.key === 'ArrowLeft' ? -1 : 1), 0, days.length - 1);
    setHover({ index, x: x(index) * rect.width / width, y: top * rect.height / height });
  };

  const hovering = hover !== null;
  useEffect(() => {
    if (!hovering) return;
    const close = (event: Event) => { if (!plotRef.current?.contains(event.target as Node)) setHover(null); };
    document.addEventListener('pointerdown', close);
    return () => document.removeEventListener('pointerdown', close);
  }, [hovering]);

  useLayoutEffect(() => {
    const root = rootRef.current, plot = plotRef.current, tip = tipRef.current;
    if (!root || !plot || !tip || !hover) return;
    const gap = 14, pad = 4;
    const rootRect = root.getBoundingClientRect(), plotRect = plot.getBoundingClientRect();
    const cx = hover.x + plotRect.left - rootRect.left, cy = hover.y + plotRect.top - rootRect.top;
    const w = tip.offsetWidth, h = tip.offsetHeight, maxLeft = root.clientWidth - w - pad, maxTop = root.clientHeight - h - pad;
    const fitsRight = cx + gap + w <= root.clientWidth - pad, fitsLeft = cx - gap - w >= pad;
    let tipLeft: number, tipTop: number;
    if (fitsRight || fitsLeft) {
      tipLeft = fitsRight ? cx + gap : cx - gap - w;
      tipTop = cy - h / 2;
    } else {
      tipLeft = cx - w / 2;
      tipTop = cy - gap - h >= pad ? cy - gap - h : cy + gap;
    }
    tip.style.transform = `translate(${clamp(tipLeft, pad, Math.max(pad, maxLeft))}px, ${clamp(tipTop, pad, Math.max(pad, maxTop))}px)`;
  });

  const axis = (item: typeof series[number], side: 'left' | 'right') => [0, 1, 2, 3].map((tick) => {
    const value = item.hi - tick * (item.hi - item.lo) / 3;
    const label = moneyMetrics.includes(item.metric) && Math.abs(value) >= 10000 ?
      `${new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 0 }).format(value / 1000)} тыс.` :
      new Intl.NumberFormat('ru-RU', { maximumFractionDigits: Math.abs(value) < 10 ? 1 : 0 }).format(value) +
      (item.metric === 'drr' || item.metric === 'ctr' ? '%' : '');
    return <text key={tick} x={side === 'left' ? left - 8 : right + 8}
      y={top + tick * (bottom - top) / 3 + 4} textAnchor={side === 'left' ? 'end' : 'start'}
      fontSize={compact ? 11 : 12} fill={metricColors[item.metric]}>{label}</text>;
  });
  const labelStep = compact ? Math.max(1, days.length - 1) : days.length <= 14 ? 1 : Math.ceil(days.length / 10);
  const previousToggle = <FormControlLabel sx={{ m: 0, whiteSpace: 'nowrap', minHeight: compact ? 40 : 36 }}
    control={<Checkbox size="small" checked={showPrevious} onChange={(_, checked) => setShowPrevious(checked)} />}
    label={<Typography variant="body2" sx={{ fontSize: compact ? 13 : 14 }}>{compact ? 'Пунктиром — предыдущий период' : 'Предыдущий период'}</Typography>} />;

  return <Box ref={rootRef} sx={{ position: 'relative' }}>
    {title ? <Typography variant="h6" fontWeight={800}>{title}</Typography> : null}
    {compact ? null : <Stack direction="row" justifyContent="space-between" alignItems="center" gap={2} sx={{ mb: .5 }}>
      <Typography variant="body2" color="text.secondary" sx={{ fontSize: 13 }}>
        Показатели на графике выбираются карточками выше · у каждого своя шкала · наведите на день, чтобы увидеть значения
      </Typography>
      {previousToggle}
    </Stack>}
    <Box ref={plotRef} tabIndex={days.length ? 0 : -1} onPointerMove={track} onPointerDown={track} onKeyDown={step}
      onPointerLeave={(event) => { if (event.pointerType === 'mouse') setHover(null); }}
        sx={{ width: '100%', touchAction: 'pan-y', outline: 'none',
        borderRadius: 1, '&:focus-visible': { boxShadow: '0 0 0 2px #007AFF' } }}>
      <svg role="img" aria-label={`График по дням: ${selectedMetrics.map((metric) => labels[metric]).join(', ')}. Стрелки влево и вправо показывают значения по дням.`}
        viewBox={`0 0 ${width} ${height}`} width="100%" style={{ display: 'block' }}>
        {[0, 1, 2, 3].map((tick) => <line key={tick} x1={left} x2={right}
          y1={top + tick * (bottom - top) / 3} y2={top + tick * (bottom - top) / 3}
          stroke={tick === 3 ? 'rgba(30,30,60,.18)' : 'rgba(30,30,60,.08)'} />)}
        {series[0] ? axis(series[0], 'left') : null}
        {!compact && series[1] ? axis(series[1], 'right') : null}
        {active !== null ? <line x1={x(active)} x2={x(active)} y1={top - 4} y2={bottom} stroke="#9A9CA3" /> : null}
        {series.map((item) => <g key={item.metric}>
          {item.previousPaths.map((path, index) => <path key={`p${index}`} d={path} fill="none"
            stroke={metricColors[item.metric]} strokeOpacity=".4" strokeWidth="2" strokeDasharray={compact ? '4 4' : '5 5'} />)}
          {item.currentPaths.map((path, index) => <path key={index} d={path} fill="none"
            stroke={metricColors[item.metric]} strokeWidth="2.5" strokeLinecap="round" />)}
          {active !== null && item.current[active] !== null ? <circle cx={x(active)}
            cy={bottom - (item.current[active]! - item.lo) / (item.hi - item.lo) * (bottom - top)} r="3.5"
            fill="#fff" stroke={metricColors[item.metric]} strokeWidth="2" /> : null}
        </g>)}
        {days.map((day, index) => {
          const last = index === days.length - 1;
          if (!last && (index % labelStep !== 0 || days.length - 1 - index < labelStep / 2)) return null;
          return <text key={day.date} x={x(index)} y={labelY} fontSize={compact ? 11 : 12} fill="#5C5C66"
            textAnchor={compact ? index === 0 ? 'start' : 'end' : 'middle'}>
            {!compact && days.length <= 7 ? dayLabel(day.date) : shortDate(day.date)}</text>;
        })}
      </svg>
    </Box>
    {active !== null ? <Box ref={tipRef} role="status" sx={{ position: 'absolute', left: 0, top: 0, zIndex: 2, pointerEvents: 'none',
      width: 'max-content', minWidth: 'min(270px, calc(100% - 8px))', maxWidth: 'calc(100% - 8px)', boxSizing: 'border-box', bgcolor: 'rgba(255,255,255,.94)',
      border: '1px solid rgba(80,90,120,.14)', borderRadius: '14px', boxShadow: '0 10px 30px rgba(40,50,90,.14)',
      backdropFilter: 'blur(18px)', px: 1.5, py: 1.25, display: 'flex', flexDirection: 'column', gap: .75, fontVariantNumeric: 'tabular-nums' }}>
      <Typography sx={{ fontSize: 13, fontWeight: 600 }}>{dayLabel(days[active].date)}
        {hasPrevious && previousDays[active] ? <Box component="span" sx={{ color: 'text.secondary', fontWeight: 400 }}>
          {` / ${dayLabel(previousDays[active].date)}`}</Box> : null}</Typography>
      {series.map((item) => <Stack key={item.metric} direction="row" justifyContent="space-between" alignItems="baseline" gap={1.25}
        sx={{ fontSize: 13 }}>
        <Stack direction="row" alignItems="center" gap={.75} sx={{ color: '#3A3A40', minWidth: 0, whiteSpace: compact ? 'normal' : 'nowrap' }}>
          <Box sx={{ flex: 'none', width: 8, height: 8, borderRadius: '2px', bgcolor: metricColors[item.metric] }} />
          {labels[item.metric]}
        </Stack>
        <Box sx={{ whiteSpace: 'nowrap' }}>
          <b>{item.current[active] === null ? 'нет данных' : formatValue(item.metric, item.current[active]!)}</b>
          {item.previous.length ? <Box component="span" sx={{ color: 'text.secondary' }}>
            {` / ${item.previous[active] === null ? '—' : formatValue(item.metric, item.previous[active]!)}`}</Box> : null}
        </Box>
      </Stack>)}
    </Box> : null}
    {!selectedMetrics.length ? <Typography color="text.secondary" textAlign="center" sx={{ mt: .5 }}>Выберите показатель в карточках выше.</Typography> :
      !series.some((item) => item.current.some((value) => value !== null)) ?
        <Typography color="text.secondary" textAlign="center" sx={{ mt: .5 }}>За этот период нет загруженных значений выбранных показателей.</Typography> : null}
    {compact ? previousToggle : days.length ? <Stack direction="row" gap={2.5} flexWrap="wrap" sx={{ mt: .5, pl: `${left / width * 100}%` }}>
      <Stack direction="row" alignItems="center" gap={1}>
        <svg width="28" height="6" aria-hidden="true"><line x1="0" y1="3" x2="28" y2="3" stroke="#8E8E93" strokeWidth="2.5" /></svg>
        <Typography variant="body2" color="text.secondary" sx={{ fontSize: 13 }}>{rangeLabel(days)}</Typography>
      </Stack>
      {hasPrevious ? <Stack direction="row" alignItems="center" gap={1}>
        <svg width="28" height="6" aria-hidden="true"><line x1="0" y1="3" x2="28" y2="3" stroke="#8E8E93" strokeOpacity=".5" strokeWidth="2" strokeDasharray="5 5" /></svg>
        <Typography variant="body2" color="text.secondary" sx={{ fontSize: 13 }}>{rangeLabel(previousDays)}</Typography>
      </Stack> : null}
    </Stack> : null}
    {hiddenPrevious || hasGaps ? <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: .5 }}>
      {hiddenPrevious ? 'Пунктир скрыт: текущий или предыдущий период загружен не полностью. ' : ''}
      {hasGaps ? 'Разрывы линии — дни без загруженных данных, а не нули.' : ''}
    </Typography> : null}
  </Box>;
}
