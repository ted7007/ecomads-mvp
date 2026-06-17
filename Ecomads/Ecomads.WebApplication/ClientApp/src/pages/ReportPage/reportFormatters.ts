import type { RecommendationDetail } from '../../shared/api/apiTypes';
import { formatMoney } from '../../shared/lib/formatMoney';

export function getRecommendationTitle(recommendation: RecommendationDetail): string {
  return recommendation.entityName || recommendation.text || 'Insight';
}

export function formatExpectedEffect(recommendation: RecommendationDetail): string {
  if (recommendation.expectedEffectMoney !== null && recommendation.expectedEffectMoney !== undefined) {
    return `${formatMoney(recommendation.expectedEffectMoney)} · ${recommendation.expectedEffectText || ''}`.trim();
  }

  return recommendation.expectedEffectText || '—';
}

export function formatActualEffect(recommendation: RecommendationDetail): string {
  if (recommendation.actualEffectStatus === 'WaitingForNextStats') {
    return 'Ожидаем следующий период';
  }

  if (recommendation.actualEffectMoney !== null && recommendation.actualEffectMoney !== undefined) {
    return `${formatMoney(recommendation.actualEffectMoney)} · ${recommendation.actualEffectText || ''}`.trim();
  }

  return recommendation.actualEffectText || '—';
}

export function getStatusView(status: string): { label: string; color: 'success' | 'warning' | 'error' | 'default' } {
  switch (status.toLowerCase()) {
    case 'accepted':
    case 'принято':
      return { label: 'В работе', color: 'success' };
    case 'pending':
    case 'postponed':
    case 'отложено':
      return { label: 'Отложено', color: 'warning' };
    case 'applied':
      return { label: 'Выполнено', color: 'success' };
    case 'rejected':
    case 'отклонено':
      return { label: 'Отклонено', color: 'error' };
    default:
      return { label: status || 'Новая', color: 'default' };
  }
}

