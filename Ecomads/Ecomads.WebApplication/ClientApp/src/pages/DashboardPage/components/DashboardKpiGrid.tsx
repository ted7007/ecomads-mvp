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

type DashboardTotals = {
  orderedAmount: number;
  spend: number;
  clicks: number;
  drr: number;
  ctr: number;
};

type KpiCardProps = {
  icon: ReactNode;
  label: string;
  value: string;
};

export function DashboardKpiGrid({ campaigns }: { campaigns: ProjectDashboard[] }) {
  const totals = calculateTotals(campaigns);

  const items: KpiCardProps[] = [
    { icon: <AttachMoneyIcon fontSize="small" />, label: 'Заказано на сумму', value: formatMoney(totals.orderedAmount) },
    { icon: <ShoppingBagIcon fontSize="small" />, label: 'Расход', value: formatMoney(totals.spend) },
    { icon: <MouseIcon fontSize="small" />, label: 'Клики', value: totals.clicks.toLocaleString('ru-RU') },
    { icon: <TargetIcon fontSize="small" />, label: 'ДРР', value: formatPercent(totals.drr, 1) },
    { icon: <TrendingUpIcon fontSize="small" />, label: 'CTR', value: formatPercent(totals.ctr, 2) }
  ];

  return (
    <Grid container spacing={2}>
      {items.map((item) => (
        <Grid item xs={12} sm={6} lg={2.4} key={item.label}>
          <KpiCard {...item} />
        </Grid>
      ))}
    </Grid>
  );
}

function KpiCard({ icon, label, value }: KpiCardProps) {
  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Stack spacing={1}>
          <Stack direction="row" alignItems="center" gap={1} color="text.secondary">
            {icon}
            <Typography variant="body2">{label}</Typography>
          </Stack>
          <Typography variant="h4" fontWeight={800}>
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
      return acc;
    },
    { spend: 0, revenue: 0, orderedAmount: 0, clicks: 0 }
  );

  const drr = totals.revenue > 0 ? (totals.spend / totals.revenue) * 100 : 0;
  const ctr =
    totals.clicks > 0
      ? campaigns.reduce((sum, item) => sum + ((item.kpi.ctr || 0) * (item.kpi.clicks || 0)), 0) / totals.clicks
      : 0;

  return {
    orderedAmount: totals.orderedAmount,
    spend: totals.spend,
    clicks: totals.clicks,
    drr,
    ctr
  };
}

