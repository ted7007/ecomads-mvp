import type { KeywordRecommendationRow } from './campaignSchemas';
import { formatMoney } from '../../shared/lib/formatMoney';
import { formatPercent } from '../../shared/lib/formatPercent';

export const statusLabels: Record<string, string> = {
  ToRemove: 'К удалению',
  NeedsAttention: 'Требует внимания',
  Effective: 'Эффективное',
  Watch: 'Наблюдать',
  LowData: 'Мало данных',
  Neutral: 'Без рекомендации'
};

export const statusStyles: Record<string, { text: string; bg: string; border: string; dot: string; rowBg: string }> = {
  ToRemove: { text: '#991B1B', bg: '#FEE2E2', border: '#FCA5A5', dot: '#DC2626', rowBg: '#FFF1F2' },
  NeedsAttention: { text: '#92400E', bg: '#FEF3C7', border: '#FCD34D', dot: '#D97706', rowBg: '#FFFBEB' },
  Effective: { text: '#065F46', bg: '#D1FAE5', border: '#6EE7B7', dot: '#059669', rowBg: '#ECFDF5' },
  Watch: { text: '#1D4ED8', bg: '#DBEAFE', border: '#93C5FD', dot: '#2563EB', rowBg: '#EFF6FF' },
  LowData: { text: '#475569', bg: '#F1F5F9', border: '#CBD5E1', dot: '#94A3B8', rowBg: '#F8FAFC' },
  Neutral: { text: '#475569', bg: '#FFFFFF', border: '#CBD5E1', dot: '#94A3B8', rowBg: '#FFFFFF' }
};

export const decisionLabels: Record<string, string> = {
  None: 'Не выбрано',
  Accepted: 'В работе',
  Postponed: 'Отложено',
  Rejected: 'Отклонено',
  Applied: 'Выполнено'
};

export function getStatusLabel(status: string | null | undefined): string {
  return statusLabels[status || 'Neutral'] || status || 'Neutral';
}

export function getStatusStyle(status: string | null | undefined) {
  return statusStyles[status || 'Neutral'] || statusStyles.Neutral;
}

export function getDecisionLabel(status: string | null | undefined): string {
  return decisionLabels[status || 'None'] || status || 'None';
}

export function getStatusColor(status: string): 'error' | 'warning' | 'success' | 'info' | 'default' {
  switch (status) {
    case 'ToRemove':
      return 'error';
    case 'NeedsAttention':
      return 'warning';
    case 'Effective':
      return 'success';
    case 'Watch':
      return 'info';
    default:
      return 'default';
  }
}

export function formatExpectedEffect(type: string, money?: number | null, text?: string): string {
  const typeLabels: Record<string, string> = {
    Saving: 'Экономия',
    AdditionalRevenue: 'Доп. выручка',
    RiskReduction: 'Снижение риска',
    NotCalculated: 'Не рассчитывается'
  };

  const parts = [typeLabels[type] || type];

  if (money !== null && money !== undefined) {
    parts.push(formatMoney(money));
  }

  if (text) {
    parts.push(text);
  }

  return parts.join(' · ');
}

export function getDrrColor(value: number | null | undefined): 'error.main' | 'success.main' | 'text.primary' {
  if (value === null || value === undefined) {
    return 'text.primary';
  }

  return Number(value) > 20 ? 'error.main' : 'success.main';
}

export function formatMetricValue(key: string, value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(Number(value))) {
    return '—';
  }

  if (['spend', 'revenue', 'wastedSpend'].includes(key)) {
    return formatMoney(value);
  }

  if (['ctr'].includes(key)) {
    return formatPercent(value, 2);
  }

  if (['drr', 'cr'].includes(key)) {
    return formatPercent(value, 1);
  }

  return Number.isInteger(value) ? value.toLocaleString('ru-RU') : value.toFixed(2);
}

export function formatMetricName(key: string): string {
  const names: Record<string, string> = {
    cr: 'CR',
    cpc: 'CPC',
    ctr: 'CTR',
    spend: 'Расход',
    revenue: 'Выручка',
    orders: 'Заказы',
    views: 'Показы',
    impressions: 'Показы',
    drr: 'ДРР',
    clicks: 'Клики',
    confidenceScore: 'Уверенность',
    wastedSpend: 'Потери',
    cpo: 'CPO',
    averageOrderValue: 'Средний чек',
    avgDailyOrders: 'Заказов в день'
  };

  return names[key] || key;
}

export function normalizeKeywordRow(row: KeywordRecommendationRow): KeywordRecommendationRow {
  const views = row.views ?? 0;
  const clicks = row.clicks ?? 0;
  const spend = row.spend ?? 0;
  const revenue = row.revenue ?? 0;

  return {
    ...row,
    views,
    clicks,
    spend,
    revenue,
    ctr: views > 0 ? (clicks / views) * 100 : row.ctr,
    drr: revenue > 0 ? (spend / revenue) * 100 : row.drr,
    orders: row.orders ?? 0
  };
}
