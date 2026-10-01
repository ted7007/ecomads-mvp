import { Stack, Typography } from '@mui/material';
import type { ProjectDashboard } from '../../../shared/api/apiTypes';
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

const allOrdersUnavailable = 'Сумма всех заказов кабинета, включая нерекламные, пока не загружается из WB. ' +
  'Рекламные заказы показаны в карточке «Заказы с рекламы».';

export function DashboardKpiGrid({ campaigns, priorCampaigns, selectedMetrics, onSelect }: { campaigns: ProjectDashboard[];
  priorCampaigns: ProjectDashboard[];
  selectedMetrics: MetricKey[]; onSelect: (metric: MetricKey) => void }) {
  const totals = calculateTotals(campaigns);
  const hasData = campaigns.some((campaign) => campaign.kpi.coverageDays > 0);
  const priorById = new Map(priorCampaigns.map((campaign) => [campaign.id, campaign]));
  const comparable = campaigns.length > 0 && campaigns.every((campaign) =>
    completeKpi(campaign) && completeKpi(priorById.get(campaign.id)));
  const previous = comparable ? calculateTotals(campaigns.map((campaign) => priorById.get(campaign.id)!)) : null;
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
        <KpiCard label="Заказы" color="#1E7BF2" value="—" note="нет данных о всех заказах" unavailableReason={allOrdersUnavailable} />
        <KpiCard label="Расход" value={hasData ? formatMoney(totals.spend) : '—'} {...toggle('spend')}
          {...comparison(totals.spend, previous?.spend, 'money', 0)} />
        <KpiCard label="ДРР от заказов" color={metricColors.drr} value="—" note="нет данных о всех заказах"
          unavailableReason={allOrdersUnavailable} />
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
