import type { ClusterRecommendationResponse } from '../../../shared/api/clusterRecommendationApi';
import { formatMoney } from '../../../shared/lib/formatMoney';
import { formatPercent } from '../../../shared/lib/formatPercent';

type Evaluation = ClusterRecommendationResponse['evaluations'][number];

// Present the server's rule results; do not evaluate the report-period figures here.
export function clusterAssessment(evaluation: Evaluation | undefined) {
  if (!evaluation) return { title: 'Нет оценки за 14 дней', reason: 'Кластер отсутствует в расчёте за эти даты.', color: 'text.secondary' };
  const rules = evaluation.rules.filter(rule => rule.enabled && rule.status !== 'disabled');
  const triggered = rules.find(rule => rule.status === 'triggered');
  if (triggered) return {
    title: triggered.ruleId === 'R03' ? 'Расход без заказанных единиц' : triggered.ruleId === 'R06' ? 'ДРР выше нормы' : 'Обнаружен сигнал проблемы',
    reason: triggered.ruleId === 'R03'
      ? `Расход ${formatMoney(evaluation.facts.spend)} · заказанных единиц ${evaluation.facts.orderedProducts ?? '—'}.`
      : triggered.ruleId === 'R06'
        ? `Расчётный ДРР ${formatPercent(evaluation.economy.drr === null ? null : evaluation.economy.drr * 100)} · норма ${formatPercent(evaluation.norm.value * 100)}. Превышение сохраняется при изменении средней цены и добавлении одной заказанной единицы.`
        : triggered.explanation,
    color: 'warning.main'
  };
  const skipped = rules.find(rule => rule.status === 'skipped');
  if (skipped) return {
    title: skipped.reasonCode === 'launch' ? 'Защита запуска' : 'Недостаточно данных для оценки',
    reason: skipped.explanation, color: 'text.secondary'
  };
  const lowSample = rules.find(rule => rule.reasonCode === 'low_sample');
  if (lowSample) return { title: 'Мало заказанных единиц для оценки ДРР', reason: lowSample.explanation, color: 'text.secondary' };
  if (!rules.length) return { title: 'Проверки отключены', reason: 'Оценка эффективности не выполнялась.', color: 'text.secondary' };
  return { title: 'Сигналы проблем не обнаружены', reason: rules.map(rule => rule.explanation).join(' '), color: 'text.secondary' };
}
