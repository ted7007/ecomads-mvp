import type { ProjectDashboard } from '../../../shared/api/apiTypes';
import { completeKpi } from '../../../shared/lib/kpiComparison';

export type CampaignSortKey = 'name' | 'status' | 'spend' | 'delta' | 'revenue' | 'drr' | 'ctr' | 'cpc';

export function campaignStatus(status: number | null | undefined) {
  return status === 9 ? 'Активна' : status === 11 ? 'Приостановлена' : status === 7 ? 'Завершена' :
    status === 4 ? 'Готова к запуску' : status == null ? 'Статус неизвестен' : `Статус WB: ${status}`;
}

export function campaignValue(campaign: ProjectDashboard, key: CampaignSortKey, prior?: ProjectDashboard): string | number | null {
  const { kpi } = campaign;
  if (key === 'name') return campaign.name;
  if (key === 'status') return campaign.wbStatus == null ? null : campaignStatus(campaign.wbStatus);
  if (key === 'delta') return completeKpi(campaign) && completeKpi(prior) && prior!.kpi.spend > 0
    ? (kpi.spend / prior!.kpi.spend - 1) * 100 : null;
  if (kpi.coverageDays <= 0) return null;
  if (key === 'drr') return kpi.revenue > 0 ? kpi.drr : null;
  if (key === 'ctr') return kpi.impressions > 0 ? kpi.ctr : null;
  if (key === 'cpc') return kpi.clicks > 0 ? kpi.spend / kpi.clicks : null;
  return kpi[key];
}

/** Missing values stay last in either direction; sort before pagination. */
export function sortCampaigns(campaigns: ProjectDashboard[], priorCampaigns: ProjectDashboard[], key: CampaignSortKey, ascending: boolean) {
  const prior = new Map(priorCampaigns.map(campaign => [campaign.id, campaign]));
  return [...campaigns].sort((left, right) => {
    const a = campaignValue(left, key, prior.get(left.id));
    const b = campaignValue(right, key, prior.get(right.id));
    if (a === null || b === null) return a === b ? left.id.localeCompare(right.id) : a === null ? 1 : -1;
    const comparison = typeof a === 'string' && typeof b === 'string' ? a.localeCompare(b, 'ru') : Number(a) - Number(b);
    return (ascending ? comparison : -comparison) || left.id.localeCompare(right.id);
  });
}
