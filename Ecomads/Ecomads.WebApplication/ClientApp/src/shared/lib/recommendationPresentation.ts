import type { RecommendationCard } from '../api/clusterRecommendationApi';
import { formatMoney } from './formatMoney';

export function recommendationMoney(card: RecommendationCard) {
  const ranking = card.ranking;
  if (ranking?.weeklyInefficientSpend == null) return null;
  const label = ranking.basis === 'spend_without_units' ? 'Расход без заказов' : 'Расход сверх нормы';
  return {
    text: `${label} ≈ ${formatMoney(ranking.weeklyInefficientSpend)}/нед.`,
    explanation: `${ranking.basis === 'spend_without_units' ? 'Расход кластера без заказанных единиц, приведённый к неделе.' : 'Недельный расход минус допустимый расход при текущей сумме заказов и норме ДРР.'} Это масштаб проблемы, а не прогноз экономии.${card.preliminary ? ' Предварительно: данные или настройки неполные.' : ''}`,
  };
}

export function recommendationLimitation(card: RecommendationCard): string | null {
  const p = card.presentation;
  if (!p) return null;
  const first = p.restrictions[0];
  if (first?.code === 'unified_below_exclusion')
    return `Для исключения при единой ставке нужен ДРР не ниже ${(card.norm.value * 200).toLocaleString('ru-RU')}%. Кластер пока остаётся под наблюдением.`;
  if (first && !['insufficient_clicks', 'bid_constraints_unknown'].includes(first.code)) return first.label;
  const clicks = p.clickEvidence;
  if (p.restrictions.some(r => r.code === 'insufficient_clicks') && clicks?.minimumClicks != null)
    return `Недостаточно кликов для исключения: ${card.facts.clicks ?? '—'} из ${clicks.minimumClicks.toLocaleString('ru-RU')}${clicks.source === 'fallback' ? ' при базовой конверсии 2%' : ''}.`;
  if (first) return first.label;
  if (p.primary.kind === 'watch' && p.control?.bidType === 'unified' && card.rule.ruleId === 'R06')
    return `Для исключения нужен ДРР не ниже ${(card.norm.value * 200).toLocaleString('ru-RU')}% и подтверждённая неключевая доля.`;
  return null;
}
