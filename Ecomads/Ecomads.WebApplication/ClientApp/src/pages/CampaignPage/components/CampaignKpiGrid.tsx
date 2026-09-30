import AttachMoneyIcon from '@mui/icons-material/AttachMoney';
import MouseIcon from '@mui/icons-material/Mouse';
import ShoppingBagIcon from '@mui/icons-material/ShoppingBag';
import TargetIcon from '@mui/icons-material/TrackChanges';
import TrendingUpIcon from '@mui/icons-material/TrendingUp';
import { Box, Card, CardContent, Stack, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import type { ProjectDashboard } from '../../../shared/api/apiTypes';
import { formatMoney } from '../../../shared/lib/formatMoney';
import { formatPercent } from '../../../shared/lib/formatPercent';
import { compareKpi, completeKpi } from '../../../shared/lib/kpiComparison';
import { metricColors } from '../../../shared/ui/DailyChart';
import type { MetricKey } from '../../../shared/ui/DailyChart';

type KpiItem = {
  metric: MetricKey;
  icon: ReactNode;
  label: string;
  value: string;
  comparison?: string;
};

export function CampaignKpiGrid({ campaign, priorCampaign, selectedMetrics, onSelect }: { campaign: ProjectDashboard | null;
  priorCampaign: ProjectDashboard | null;
  selectedMetrics: MetricKey[]; onSelect: (metric: MetricKey) => void }) {
  const kpi = campaign?.kpi;
  const previous = priorCampaign?.kpi;
  const comparable = completeKpi(campaign) && completeKpi(priorCampaign);
  const comparison = (current: number | null | undefined, before: number | null | undefined, unit: 'money' | 'percent') => {
    if (!comparable || current == null || before == null) return undefined;
    const value = compareKpi(current, before, unit);
    return `${value.delta} · было ${value.previous}`;
  };
  const items: KpiItem[] = [
    { metric: 'revenue', icon: <AttachMoneyIcon fontSize="small" />, label: 'Заказы с рекламы', value: kpi?.coverageDays ? formatMoney(kpi.revenue) : '—',
      comparison: comparison(kpi?.revenue, previous?.revenue, 'money') },
    { metric: 'spend', icon: <ShoppingBagIcon fontSize="small" />, label: 'Расход', value: kpi?.coverageDays ? formatMoney(kpi.spend) : '—',
      comparison: comparison(kpi?.spend, previous?.spend, 'money') },
    { metric: 'drr', icon: <TargetIcon fontSize="small" />, label: 'ДРР рекламы', value: kpi && kpi.revenue > 0 ? formatPercent(kpi.drr, 1) : '—',
      comparison: comparison(kpi?.revenue ? kpi.drr : null, previous?.revenue ? previous.drr : null, 'percent') },
    { metric: 'ctr', icon: <TrendingUpIcon fontSize="small" />, label: 'CTR', value: kpi?.impressions ? formatPercent(kpi.ctr, 2) : '—',
      comparison: comparison(kpi?.impressions ? kpi.ctr : null, previous?.impressions ? previous.ctr : null, 'percent') },
    { metric: 'cpc', icon: <MouseIcon fontSize="small" />, label: 'CPC', value: kpi?.clicks ? formatMoney(kpi.spend / kpi.clicks) : '—',
      comparison: comparison(kpi?.clicks ? kpi.spend / kpi.clicks : null, previous?.clicks ? previous.spend / previous.clicks : null, 'money') }
  ];

  return (
    <Box
      sx={{
        display: 'grid',
        gridTemplateColumns: {
          xs: 'repeat(2, minmax(0, 1fr))',
          sm: 'repeat(2, minmax(0, 1fr))',
          md: 'repeat(3, minmax(0, 1fr))',
          lg: 'repeat(5, minmax(0, 1fr))'
        },
        gap: 2,
        width: '100%'
      }}
    >
      {items.map((item) => (
        <Box key={item.label} sx={{ minWidth: 0 }}>
          <Card onClick={() => onSelect(item.metric)} role="button" tabIndex={0} onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') onSelect(item.metric); }}
            aria-pressed={selectedMetrics.includes(item.metric)} sx={{ height: '100%', cursor: 'pointer', border: selectedMetrics.includes(item.metric) ? `2px solid ${metricColors[item.metric]}` : undefined }}>
            <CardContent sx={{ p: 2, '&:last-child': { pb: 2 } }}>
              <Stack spacing={1}>
                <Stack direction="row" alignItems="center" gap={1} color="text.secondary">
                  {item.icon}
                  <Typography variant="body2">{item.label}</Typography>
                </Stack>
                <Typography variant="h4" fontWeight={800} sx={{ lineHeight: 1.15, fontSize: { xs: 22, md: 28 }, overflowWrap: 'anywhere' }}>
                  {item.value}
                </Typography>
                <Typography variant="caption" color="text.secondary" sx={{ minHeight: 20 }}>
                  {item.comparison ?? 'Сравнение недоступно'}
                </Typography>
              </Stack>
            </CardContent>
          </Card>
        </Box>
      ))}
    </Box>
  );
}
