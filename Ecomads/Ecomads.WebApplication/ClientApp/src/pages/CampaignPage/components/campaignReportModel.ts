import type { WbJamRow, WbClusterRow } from '../campaignApi';
import type { ClusterRecommendationResponse, RecommendationCard } from '../../../shared/api/clusterRecommendationApi';

export const pairKey = (article: string, text: string) => JSON.stringify([article, text.trim().toLocaleLowerCase('ru-RU')]);
export const clusterCtr = (row: Pick<WbClusterRow, 'views' | 'clicks'>) => row.views != null && row.views > 0 && row.clicks != null ? row.clicks / row.views * 100 : null;
export const jamCandidates = (rows: WbJamRow[]) => rows.filter(row => row.orders != null && row.orders > 0 && !row.matchingLoadedAdCluster);
export function packageCards(report: ClusterRecommendationResponse, ids: string[]) {
  return [...new Map(report.cards.filter(card => ids.includes(card.evaluationId) && card.rule.ruleId === 'R03')
    .map(card => [card.evaluationId, card])).values()];
}

type RecommendationItem = { kind: 'single'; id: string; card: RecommendationCard } | { kind: 'package'; id: string; cards: RecommendationCard[] };
export function recommendationItems(report: ClusterRecommendationResponse): RecommendationItem[] {
  const bundles = (report.packages ?? []).map(b => ({ ...b, cards: packageCards(report, b.evaluationIds) })).filter(b => b.cards.length);
  const emitted = new Set<string>();
  const cards = [...new Map(report.cards.map(c => [`${c.evaluationId}:${c.rule.ruleId}`, c])).values()];
  return cards.flatMap<RecommendationItem>(card => {
    const bundle = bundles.find(b => b.cards.some(c => c.evaluationId === card.evaluationId && c.rule.ruleId === card.rule.ruleId));
    if (!bundle) return [{ kind: 'single' as const, id: `${card.evaluationId}:${card.rule.ruleId}`, card }];
    if (emitted.has(bundle.id)) return [];
    emitted.add(bundle.id);
    return [{ kind: 'package' as const, id: bundle.id, cards: bundle.cards }];
  });
}
