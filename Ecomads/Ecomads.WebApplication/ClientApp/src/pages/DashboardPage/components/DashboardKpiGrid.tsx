import AttachMoneyIcon from '@mui/icons-material/AttachMoney';
import MouseIcon from '@mui/icons-material/Mouse';
import ShoppingBagIcon from '@mui/icons-material/ShoppingBag';
import TargetIcon from '@mui/icons-material/TrackChanges';
import TrendingUpIcon from '@mui/icons-material/TrendingUp';
import { Card, CardContent, Grid, Stack, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import type { ProjectDashboard } from '../../../shared/api/apiTypes';
import { formatMoney } from '../../../shared/lib/formatMoney';
import { formatPercent } from '../../../shared/lib/formatPercent';
import { metricColors } from '../../../shared/ui/DailyChart';
import type { MetricKey } from '../../../shared/ui/DailyChart';

type DashboardTotals = {
  orderedAmount: number;
  revenue: number;
  spend: number;
  clicks: number;
  impressions: number;
  drr: number | null;
  ctr: number;
};

type KpiCardProps = {
  keyMetric: MetricKey;
  icon: ReactNode;
  label: string;
  value: string;
};

export function DashboardKpiGrid({ campaigns, selectedMetric, onSelect }: { campaigns: ProjectDashboard[];
  selectedMetric: MetricKey; onSelect: (metric: MetricKey) => void }) {
  const totals = calculateTotals(campaigns);
  const hasData = campaigns.some((campaign) => campaign.kpi.coverageDays > 0);

  const items: KpiCardProps[] = [
    { keyMetric: 'revenue', icon: <AttachMoneyIcon fontSize="small" />, label: 'Заказы с рекламы', value: hasData ? formatMoney(totals.revenue) : '—' },
    { keyMetric: 'spend', icon: <ShoppingBagIcon fontSize="small" />, label: 'Расход', value: hasData ? formatMoney(totals.spend) : '—' },
    { keyMetric: 'drr', icon: <TargetIcon fontSize="small" />, label: 'ДРР рекламы', value: totals.drr === null ? '—' : formatPercent(totals.drr, 1) },
    { keyMetric: 'ctr', icon: <TrendingUpIcon fontSize="small" />, label: 'CTR', value: totals.impressions > 0 ? formatPercent(totals.ctr, 2) : '—' },
    { keyMetric: 'clicks', icon: <MouseIcon fontSize="small" />, label: 'Клики', value: hasData ? totals.clicks.toLocaleString('ru-RU') : '—' }
  ];

  return (
    <Grid container spacing={2}>
      {items.map((item) => (
        <Grid item xs={6} md={4} lg={2.4} key={item.label}>
          <KpiCard {...item} selected={selectedMetric === item.keyMetric} onSelect={() => onSelect(item.keyMetric)} />
        </Grid>
      ))}
    </Grid>
  );
}

function KpiCard({ keyMetric, icon, label, value, selected, onSelect }: KpiCardProps & { selected: boolean; onSelect: () => void }) {
  return (
    <Card onClick={onSelect} role="button" tabIndex={0} onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') onSelect(); }}
      aria-pressed={selected} sx={{ height: '100%', cursor: 'pointer', border: selected ? `2px solid ${metricColors[keyMetric]}` : undefined }}>
      <CardContent sx={{ p: 2, '&:last-child': { pb: 2 } }}>
        <Stack spacing={0.5}>
          <Stack direction="row" alignItems="center" gap={1} color="text.secondary">
            {icon}
            <Typography variant="body2">{label}</Typography>
          </Stack>
          <Typography variant="h4" fontWeight={800} sx={{ fontSize: { xs: 22, sm: 28 }, overflowWrap: 'anywhere' }}>
            {value}
          </Typography>
        </Stack>
      </CardContent>
    </Card>
  );
}

function calculateTotals(campaigns: ProjectDashboard[]): DashboardTotals {
  const totals = campaigns.reduce(
    (acc, item) => {
      acc.spend += item.kpi.spend || 0;
      acc.revenue += item.kpi.revenue || 0;
      acc.orderedAmount += item.kpi.orderedAmount || 0;
      acc.clicks += item.kpi.clicks || 0;
      acc.impressions += item.kpi.impressions || 0;
      return acc;
    },
    { spend: 0, revenue: 0, orderedAmount: 0, clicks: 0, impressions: 0 }
  );

  const drr = totals.revenue > 0 ? (totals.spend / totals.revenue) * 100 : null;
  const ctr = totals.impressions > 0 ? totals.clicks * 100 / totals.impressions : 0;

  return {
    revenue: totals.revenue,
    orderedAmount: totals.orderedAmount,
    spend: totals.spend,
    clicks: totals.clicks,
    impressions: totals.impressions,
    drr,
    ctr
  };
}

