import type { WbJamRow, WbClusterRow } from '../campaignApi';
import type { ClusterRecommendationResponse } from '../../../shared/api/clusterRecommendationApi';

export const pairKey = (article: string, text: string) => JSON.stringify([article, text.trim().toLocaleLowerCase('ru-RU')]);
export const clusterCtr = (row: Pick<WbClusterRow, 'views' | 'clicks'>) => row.views != null && row.views > 0 && row.clicks != null ? row.clicks / row.views * 100 : null;
export const jamCandidates = (rows: WbJamRow[]) => rows.filter(row => row.orders != null && row.orders > 0 && !row.matchingLoadedAdCluster);
export function packageCards(report: ClusterRecommendationResponse, ids: string[]) {
  return [...new Map(report.cards.filter(card => ids.includes(card.evaluationId) && card.rule.ruleId === 'R03')
    .map(card => [card.evaluationId, card])).values()];
}
