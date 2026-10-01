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

export function CampaignKpiGrid({ campaign, priorCampaign, selectedMetrics, onSelect }: { campaign: ProjectDashboard | null;
  priorCampaign: ProjectDashboard | null;
  selectedMetrics: MetricKey[]; onSelect: (metric: MetricKey) => void }) {
  const kpi = campaign?.kpi;
  const previous = priorCampaign?.kpi;
  const hasData = !!kpi?.coverageDays;
  const comparable = completeKpi(campaign) && completeKpi(priorCampaign);
  const comparison = (current: number | null | undefined, before: number | null | undefined, unit: KpiUnit,
    direction: KpiDirection): Pick<KpiCardProps, 'delta' | 'deltaTone' | 'previous'> => {
    if (!comparable || current == null || before == null) return {};
    const value = compareKpi(current, before, unit, direction);
    return { delta: value.delta, deltaTone: value.tone, previous: value.previous };
  };
  const toggle = (metric: MetricKey) => ({ color: metricColors[metric], selected: selectedMetrics.includes(metric),
    onToggle: () => onSelect(metric) });
  const drr = kpi && kpi.revenue > 0 ? kpi.drr : null;
  const cpc = kpi?.clicks ? kpi.spend / kpi.clicks : null;
  const drrComparison = comparison(drr, previous?.revenue ? previous.drr : null, 'percent', -1);

  return (
    <Stack spacing={0.5}>
      <KpiGrid>
        <KpiCard label="Заказы с рекламы" value={hasData ? formatMoney(kpi!.revenue) : '—'} {...toggle('revenue')}
          {...comparison(kpi?.revenue, previous?.revenue, 'money', 1)} />
        <KpiCard label="Расход" value={hasData ? formatMoney(kpi!.spend) : '—'} {...toggle('spend')}
          {...comparison(kpi?.spend, previous?.spend, 'money', 0)} />
        <KpiCard label="ДРР от рекламы" value={drr === null ? '—' : formatPercent(drr, 1)} {...toggle('drr')}
          delta={drrComparison.delta} deltaTone={drrComparison.deltaTone}
          valueColor={drr !== null && campaign && drr > campaign.targetDrr ? 'warning.main' : undefined}
          note={campaign ? `норма ${formatPercent(campaign.targetDrr, campaign.targetDrr % 1 ? 1 : 0)}` : undefined} />
        <KpiCard label="CTR" value={kpi?.impressions ? formatPercent(kpi.ctr, 1) : '—'} {...toggle('ctr')}
          {...comparison(kpi?.impressions ? kpi.ctr : null, previous?.impressions ? previous.ctr : null, 'percent', 1)} />
        <KpiCard label="CPC" value={cpc === null ? '—' : formatMoney(cpc, 1)} {...toggle('cpc')}
          {...comparison(cpc, previous?.clicks ? previous.spend / previous.clicks : null, 'cpc', -1)} />
      </KpiGrid>
      {hasData && !comparable ? <Typography variant="caption" color="text.secondary" sx={{ px: 0.5 }}>
        Сравнение с прошлым периодом появится после полной загрузки обоих периодов.
      </Typography> : null}
    </Stack>
  );
}
