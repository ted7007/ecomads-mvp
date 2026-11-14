import type { ClusterRecommendationResponse } from '../../../shared/api/clusterRecommendationApi';
import { recommendationAction, recommendationLimitations, recommendationReason, recommendationTitle } from '../../../shared/lib/recommendationPresentation';

type Evaluation = ClusterRecommendationResponse['evaluations'][number];

// Present the server's rule results; do not evaluate the report-period figures here.
export function clusterAssessment(evaluation: Evaluation | undefined) {
  if (!evaluation) return { title: 'Нет оценки за 14 дней', reason: 'Кластер отсутствует в расчёте за эти даты.', color: 'text.secondary' };
  const rules = evaluation.rules.filter(rule => rule.enabled && rule.status !== 'disabled');
  const triggered = rules.find(rule => rule.status === 'triggered');
  if (triggered) {
    const evidence = { ...evaluation, rule: triggered };
    return {
      title: recommendationTitle(evidence), reason: recommendationReason(evidence), color: 'warning.main',
      action: recommendationAction(evidence), limitations: recommendationLimitations(evidence, evaluation.quality)
    };
  }
  const skipped = rules.find(rule => rule.status === 'skipped');
  if (skipped) return {
    title: skipped.reasonCode === 'launch' ? 'Защита запуска' : 'Недостаточно данных для оценки',
    reason: skipped.reasonCode === 'launch' ? 'Пока идёт период запуска кампании.'
      : evaluation.quality.availableDays != null && evaluation.quality.expectedDays != null &&
        evaluation.quality.availableDays < evaluation.quality.expectedDays
        ? `Данные подтверждены за ${evaluation.quality.availableDays} из ${evaluation.quality.expectedDays} дней.`
        : 'Пока не удалось подтвердить данные для оценки.',
    color: 'text.secondary'
  };
  const lowSample = rules.find(rule => rule.reasonCode === 'low_sample');
  if (lowSample) return { title: 'Мало заказанных единиц для оценки ДРР', reason: '', color: 'text.secondary' };
  if (!rules.length) return { title: 'Проверки отключены', reason: 'Оценка эффективности не выполнялась.', color: 'text.secondary' };
  return { title: 'Отклонений не найдено', reason: '', color: 'text.secondary' };
}
