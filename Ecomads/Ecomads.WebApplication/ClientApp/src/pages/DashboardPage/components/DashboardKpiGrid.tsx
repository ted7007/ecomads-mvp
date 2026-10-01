import { Stack, Typography } from '@mui/material';
import type { ProjectDashboard } from '../../../shared/api/apiTypes';
import type { DailyPoint } from '../dashboardApi';
import { formatMoney } from '../../../shared/lib/formatMoney';
import { formatPercent } from '../../../shared/lib/formatPercent';
import { compareKpi, completeKpi } from '../../../shared/lib/kpiComparison';
import type { KpiDirection, KpiUnit } from '../../../shared/lib/kpiComparison';
import { metricColors } from '../../../shared/ui/DailyChart';
import type { MetricKey } from '../../../shared/ui/DailyChart';
import { KpiCard, KpiGrid } from '../../../shared/ui/KpiCard';
import type { KpiCardProps } from '../../../shared/ui/KpiCard';

type DashboardTotals = {
  revenue: number;
  spend: number;
  clicks: number;
  impressions: number;
  ctr: number;
};

const ordersHint = 'История всех заказов копится с первой загрузки; WB отдаёт по дням только последние 7 дней.';
const orderTotal = (days: DailyPoint[]): number | null => days.length > 0 &&
  days.every((day) => day.totalOrderSum != null)
    ? days.reduce((sum, day) => sum + day.totalOrderSum!, 0) : null;

export function DashboardKpiGrid({ campaigns, priorCampaigns, days, previousDays, selectedMetrics, onSelect }: { campaigns: ProjectDashboard[];
  priorCampaigns: ProjectDashboard[];
  days: DailyPoint[]; previousDays: DailyPoint[];
  selectedMetrics: MetricKey[]; onSelect: (metric: MetricKey) => void }) {
  const totals = calculateTotals(campaigns);
  const hasData = campaigns.some((campaign) => campaign.kpi.coverageDays > 0);
  const priorById = new Map(priorCampaigns.map((campaign) => [campaign.id, campaign]));
  const comparable = campaigns.length > 0 && campaigns.every((campaign) =>
    completeKpi(campaign) && completeKpi(priorById.get(campaign.id)));
  const previous = comparable ? calculateTotals(campaigns.map((campaign) => priorById.get(campaign.id)!)) : null;
  const currentOrders = orderTotal(days);
  const priorOrders = orderTotal(previousDays);
  const loadedOrderDays = days.filter((day) => day.totalOrderSum != null).length;
  const ordersNote = currentOrders === null ? `загружено ${loadedOrderDays} из ${days.length} дней` : undefined;
  const drrTotal = currentOrders && campaigns.length > 0 && campaigns.every(completeKpi)
    ? totals.spend / currentOrders * 100 : null;
  const priorDrrTotal = priorOrders && previous && previousDays.length === days.length
    ? previous.spend / priorOrders * 100 : null;
  const orderComparison = currentOrders !== null && priorOrders !== null && previousDays.length === days.length
    ? compareKpi(currentOrders, priorOrders, 'money', 1) : null;
  const drrTotalComparison = drrTotal !== null && priorDrrTotal !== null
    ? compareKpi(drrTotal, priorDrrTotal, 'percent', -1) : null;
  const comparison = (current: number | null, before: number | null | undefined, unit: KpiUnit,
    direction: KpiDirection): Pick<KpiCardProps, 'delta' | 'deltaTone' | 'previous'> => {
    if (current === null || before == null || !comparable) return {};
    const value = compareKpi(current, before, unit, direction);
    return { delta: value.delta, deltaTone: value.tone, previous: value.previous };
  };
  const toggle = (metric: MetricKey) => ({ color: metricColors[metric], selected: selectedMetrics.includes(metric),
    onToggle: () => onSelect(metric) });

  return (
    <Stack spacing={0.5}>
      <KpiGrid>
        <KpiCard label="Заказы" value={currentOrders === null ? '—' : formatMoney(currentOrders)}
          note={ordersNote} hint={currentOrders === null ? ordersHint : undefined} {...toggle('orders')}
          delta={orderComparison?.delta} deltaTone={orderComparison?.tone} previous={orderComparison?.previous} />
        <KpiCard label="Расход" value={hasData ? formatMoney(totals.spend) : '—'} {...toggle('spend')}
          {...comparison(totals.spend, previous?.spend, 'money', 0)} />
        <KpiCard label="ДРР от заказов" value={drrTotal === null ? '—' : formatPercent(drrTotal, 1)}
          note={drrTotal === null ? ordersNote ?? 'рекламная статистика загружена не полностью' : undefined}
          hint={drrTotal === null ? ordersHint : undefined} {...toggle('drrTotal')}
          delta={drrTotalComparison?.delta} deltaTone={drrTotalComparison?.tone} previous={drrTotalComparison?.previous} />
        <KpiCard label="Заказы с рекламы" value={hasData ? formatMoney(totals.revenue) : '—'} {...toggle('revenue')}
          {...comparison(totals.revenue, previous?.revenue, 'money', 1)} />
        <KpiCard label="CTR" value={totals.impressions > 0 ? formatPercent(totals.ctr, 1) : '—'} {...toggle('ctr')}
          {...comparison(totals.impressions > 0 ? totals.ctr : null, previous?.impressions ? previous.ctr : null, 'percent', 1)} />
      </KpiGrid>
      {hasData && !comparable ? <Typography variant="caption" color="text.secondary" sx={{ px: 0.5 }}>
        Сравнение с прошлым периодом появится после полной загрузки обоих периодов.
      </Typography> : null}
    </Stack>
  );
}

function calculateTotals(campaigns: ProjectDashboard[]): DashboardTotals {
  const totals = campaigns.reduce(
    (acc, item) => {
      acc.spend += item.kpi.spend || 0;
      acc.revenue += item.kpi.revenue || 0;
      acc.clicks += item.kpi.clicks || 0;
      acc.impressions += item.kpi.impressions || 0;
      return acc;
    },
    { spend: 0, revenue: 0, clicks: 0, impressions: 0 }
  );

  return {
    ...totals,
    ctr: totals.impressions > 0 ? totals.clicks * 100 / totals.impressions : 0
  };
}
