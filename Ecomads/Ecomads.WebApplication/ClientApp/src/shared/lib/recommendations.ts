import type { ProjectDashboard } from '../api/apiTypes';
import type { WbClusterRow } from '../../pages/CampaignPage/campaignApi';
import { formatMoney } from './formatMoney';
import { formatPercent } from './formatPercent';

export type Recommendation = { id: string; severity: 'warning' | 'info'; title: string; detail: string; action: string };

export function campaignRecommendations(campaigns: ProjectDashboard[]): Recommendation[] {
  return campaigns.flatMap((campaign) => {
    const kpi = campaign.kpi;
    if (campaign.wbStatus !== 9 || !kpi.coverageDays) return [];
    const partial = kpi.coverageDays < kpi.expectedDays;
    const qualifier = partial ? ` Вывод предварительный: загружено ${kpi.coverageDays} из ${kpi.expectedDays} дней.` : '';
    const items: Recommendation[] = [];
    if (kpi.revenue > 0 && kpi.drr > campaign.targetDrr) items.push({
      id: `drr:${campaign.id}`, severity: 'warning', title: campaign.name,
      detail: `ДРР от рекламы ${formatPercent(kpi.drr, 1)} при норме ${formatPercent(campaign.targetDrr, 1)} · расход ${formatMoney(kpi.spend)}.${qualifier}`,
      action: partial ? 'Проверьте ставки и состав кампании после завершения загрузки периода.' :
        'Проверьте ставки и состав кампании.'
    });
    if (kpi.impressions > 0 && kpi.ctr < campaign.minCtr) items.push({
      id: `ctr:${campaign.id}`, severity: 'warning', title: campaign.name,
      detail: `CTR ${formatPercent(kpi.ctr, 1)} при норме ${formatPercent(campaign.minCtr, 1)}.${qualifier}`,
      action: 'Проверьте объявления и соответствие запросам.'
    });
    return items;
  }).sort((left, right) => left.title.localeCompare(right.title, 'ru'));
}

export function clusterRecommendations(campaignId: string, rows: WbClusterRow[]): Recommendation[] {
  return rows.filter((row) => row.assessment === 'Расход без заказов — проверить').map((row) => ({
    id: `cluster:${campaignId}:${row.nomenclatureId}:${stableHash(row.clusterName)}`, severity: 'warning' as const,
    title: row.clusterName,
    detail: `Артикул ${row.nomenclatureId}: ${formatMoney(row.spend)} расхода, ${row.clicks} кликов, 0 заказов за выбранный период.`,
    action: 'Проверьте релевантность кластера, карточку товара и настройки рекламы в WB. Данных о выручке кластера нет.'
  }));
}

function stableHash(text: string): string {
  let value = 2166136261;
  for (let index = 0; index < text.length; index++) {
    value ^= text.charCodeAt(index);
    value = Math.imul(value, 16777619);
  }
  return (value >>> 0).toString(16);
}
