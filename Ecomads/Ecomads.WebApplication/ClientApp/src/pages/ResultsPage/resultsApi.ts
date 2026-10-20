import { z } from 'zod';
import { httpClient } from '../../shared/api/httpClient';

const status = z.enum(['pending', 'historical', 'snoozed', 'irrelevant', 'not_applied', 'measuring', 'confirmed', 'not_confirmed', 'not_evaluated', 'redistributed', 'done', 'no_effect']);
const member = z.object({ recommendationId: z.string().uuid(), ruleId: z.string(), object: z.string(), action: z.string(),
  decision: z.string().nullable(), reason: z.string().nullable(), snoozeUntil: z.string().nullable(), firstShownAtUtc: z.string(), legacy: z.boolean() });
const row = z.object({ id: z.string().uuid(), campaign: z.string(), status, reason: z.string(), savings: z.number().nullable(), forecast: z.number().nullable(),
  appliedDate: z.string().nullable(), expectedDate: z.string().nullable(), revision: z.number(), members: z.array(member) });
const report = z.object({ enabled: z.boolean(), collectionStartedAtUtc: z.string().nullable(), startDate: z.string().nullable(), endDate: z.string(),
  summary: z.object({ savings: z.number(), confirmedMeasurements: z.number(), confirmedRecommendations: z.number(), forecast: z.number(), measuring: z.number(),
    ordersBefore: z.number().nullable(), ordersAfter: z.number().nullable(), ordersReason: z.string().nullable() }),
  funnel: z.object({ issued: z.number(), accepted: z.number(), applied: z.number(), confirmed: z.number(), appliedWithoutAcceptance: z.number(),
    snoozed: z.number(), irrelevant: z.number(), undecided: z.number(), rejectionReasons: z.record(z.number()) }),
  weeks: z.array(z.object({ start: z.string(), savings: z.number(), cumulative: z.number() })), rows: z.array(row), total: z.number(), page: z.number(), pageSize: z.number() });
const objectMetrics = z.object({ key: z.string(), article: z.string(), spend: z.number(), clicks: z.number(), units: z.number(), revenue: z.number().nullable(),
  totalUnits: z.number().nullable(), unitPrice: z.number().nullable(), knownStockDays: z.number(), zeroStockDays: z.number(), pausedZeroDays: z.number() });
const metrics = z.object({ campaignSpend: z.number(), campaignRevenue: z.number(), otherCampaignSpend: z.number(), campaignDrr: z.number().nullable(),
  objects: z.array(objectMetrics), gaps: z.array(z.string()), sources: z.array(z.string()) });
const period = z.object({ start: z.string(), end: z.string() });
const detail = z.object({ row, beforeWindow: period.nullable(), afterWindow: period.nullable(),
  applications: z.array(z.object({ recommendationId: z.string(), exposureId: z.string(),
    before: z.object({ id: z.string(), atUtc: z.string(), source: z.string(), state: z.string() }),
    applied: z.object({ id: z.string(), atUtc: z.string(), source: z.string(), state: z.string() }) })),
  revisions: z.array(z.object({ number: z.number(), calculatedAtUtc: z.string(), evidence: z.object({ version: z.number(), before: metrics, after: metrics,
    outcome: z.object({ status: z.string(), reason: z.string(), savings: z.number().nullable(), formula: z.string() }), restrictions: z.array(z.string()) }) })) });
export type ResultsReport = z.infer<typeof report>;
export type ResultRow = z.infer<typeof row>;
export type ResultMetrics = z.infer<typeof metrics>;
export type ResultsFilters = { storeId?: string; period?: string; filter?: string; page?: number; pageSize?: number };
export async function getResults(filters: ResultsFilters) {
  const params = new URLSearchParams();
  Object.entries(filters).forEach(([key, value]) => { if (value !== undefined && value !== '') params.set(key, String(value)); });
  return report.parse(await httpClient<unknown>(`/api/wb/results?${params}`));
}
export async function getResultDetails(id: string) { return detail.parse(await httpClient<unknown>(`/api/wb/results/${encodeURIComponent(id)}`)); }
export const resultLabels: Record<ResultRow['status'], string> = { pending: 'Применение не подтверждено', historical: 'История решения', snoozed: 'Отложено',
  irrelevant: 'Не актуально', not_applied: 'Не применено', measuring: 'На замере', confirmed: 'Подтверждена', not_confirmed: 'Не подтвердилась',
  not_evaluated: 'Не оценено', redistributed: 'Перераспределено', done: 'Выполнено', no_effect: 'Нет эффекта' };
export function resultDate(date: string | null) { return date ? date.includes('T') ? new Date(date).toLocaleDateString('ru-RU', { timeZone: 'Europe/Moscow' }) : date.split('-').reverse().join('.') : '—'; }
