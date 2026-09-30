import type { ProjectDashboard } from '../api/apiTypes';
import type { WbClusterRow } from '../../pages/CampaignPage/campaignApi';
import { formatMoney } from './formatMoney';
import { formatPercent } from './formatPercent';

export type Recommendation = { id: string; severity: 'warning' | 'info'; title: string; detail: string; action: string };

export function campaignRecommendations(campaigns: ProjectDashboard[]): Recommendation[] {
  return campaigns.flatMap((campaign) => {
    const kpi = campaign.kpi;
    if (!kpi.coverageDays || kpi.coverageDays < kpi.expectedDays || kpi.revenue <= 0 ||
      kpi.drr <= campaign.targetDrr) return [];
    return [{ id: `drr:${campaign.id}`, severity: 'warning' as const,
      title: campaign.name,
      detail: `ДРР рекламы ${formatPercent(kpi.drr, 1)} при цели ${formatPercent(campaign.targetDrr, 1)}. Расход ${formatMoney(kpi.spend)} за полностью загруженный период.`,
      action: 'Проверьте расходы, ставки и состав кампании в кабинете WB.' }];
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
