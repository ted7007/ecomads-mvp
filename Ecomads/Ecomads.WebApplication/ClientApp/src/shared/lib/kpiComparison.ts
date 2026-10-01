import type { ProjectDashboard } from '../api/apiTypes';
import { formatMoney } from './formatMoney';
import { formatPercent } from './formatPercent';

export type KpiTone = 'good' | 'bad' | 'neutral';
/** 1 — рост лучше, -1 — снижение лучше, 0 — без оценки. */
export type KpiDirection = 1 | -1 | 0;
export type KpiUnit = 'money' | 'percent' | 'count' | 'cpc';

export const completeKpi = (campaign: ProjectDashboard | null | undefined) =>
  !!campaign && campaign.kpi.expectedDays > 0 && campaign.kpi.coverageDays === campaign.kpi.expectedDays;

export const formatKpiValue = (value: number, unit: KpiUnit) => unit === 'money' ? formatMoney(value) :
  unit === 'cpc' ? formatMoney(value, 1) : unit === 'percent' ? formatPercent(value, 1) : value.toLocaleString('ru-RU');

export function compareKpi(current: number, previous: number, unit: KpiUnit, direction: KpiDirection = 0) {
  const previousLabel = formatKpiValue(previous, unit);
  const tone = (change: number): KpiTone => direction === 0 || change === 0 ? 'neutral' :
    Math.sign(change) === direction ? 'good' : 'bad';
  if (unit === 'percent') {
    const difference = current - previous;
    return { delta: `${difference > 0 ? '+' : difference < 0 ? '−' : ''}${Math.abs(difference).toLocaleString('ru-RU', { maximumFractionDigits: 1 })} п.п.`,
      previous: previousLabel, tone: tone(difference) };
  }
  if (!previous) return { delta: '—', previous: previousLabel, tone: 'neutral' as KpiTone };
  const difference = (current / previous - 1) * 100;
  return { delta: `${difference > 0 ? '+' : difference < 0 ? '−' : ''}${Math.abs(difference).toLocaleString('ru-RU', { maximumFractionDigits: 0 })}%`,
    previous: previousLabel, tone: tone(difference) };
}
