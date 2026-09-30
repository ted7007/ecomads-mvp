import type { ProjectDashboard } from '../api/apiTypes';
import { formatMoney } from './formatMoney';
import { formatPercent } from './formatPercent';

export const completeKpi = (campaign: ProjectDashboard | null | undefined) =>
  !!campaign && campaign.kpi.expectedDays > 0 && campaign.kpi.coverageDays === campaign.kpi.expectedDays;

export function compareKpi(current: number, previous: number, unit: 'money' | 'percent' | 'count') {
  const previousLabel = unit === 'money' ? formatMoney(previous) :
    unit === 'percent' ? formatPercent(previous, 1) : previous.toLocaleString('ru-RU');
  if (unit === 'percent') {
    const difference = current - previous;
    return { delta: `${difference > 0 ? '+' : difference < 0 ? '−' : ''}${Math.abs(difference).toLocaleString('ru-RU', { maximumFractionDigits: 1 })} п.п.`,
      previous: previousLabel };
  }
  if (!previous) return { delta: '—', previous: previousLabel };
  const difference = (current / previous - 1) * 100;
  return { delta: `${difference > 0 ? '+' : difference < 0 ? '−' : ''}${Math.abs(difference).toLocaleString('ru-RU', { maximumFractionDigits: 0 })}%`,
    previous: previousLabel };
}
