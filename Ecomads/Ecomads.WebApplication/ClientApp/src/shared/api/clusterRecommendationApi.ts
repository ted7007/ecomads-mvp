import { z } from 'zod';
import { httpClient } from './httpClient';
import { wbClusterRowSchema } from '../../pages/CampaignPage/campaignApi';
import type { DashboardFilters } from '../../pages/DashboardPage/dashboardApi';

const facts = z.object({ spend: z.number(), views: z.number().nullable(), clicks: z.number().nullable(),
  carts: z.number().nullable(), orders: z.number().nullable(), orderedProducts: z.number().nullable(), unitPrice: z.number().nullable() });
const norm = z.object({ value: z.number(), source: z.enum(['article', 'campaign', 'store', 'default']),
  storeVersion: z.number(), campaignVersion: z.number(), articleVersion: z.number() });
const economy = z.object({ estimatedOrderSum: z.number().nullable(), drr: z.number().nullable(),
  drrMin: z.number().nullable(), drrMax: z.number().nullable(), drrPlusOne: z.number().nullable(), allowedSpendPerUnit: z.number().nullable() });
const subject = z.object({ storeId: z.string().uuid(), campaignId: z.string().uuid(), articleId: z.string().uuid(),
  nomenclatureId: z.string(), nomenclatureName: z.string(), clusterName: z.string() });
const rule = z.object({ ruleId: z.string(), ruleVersion: z.number(), group: z.number(), enabled: z.boolean(),
  status: z.enum(['triggered', 'not_triggered', 'skipped', 'disabled']), reasonCode: z.string(), explanation: z.string(),
  threshold: z.number().nullable(), actionKind: z.string(), tier: z.string() });
const action = z.object({ kind: z.string(), label: z.string(), reductionPercent: z.number().nullable(), targetBid: z.number().nullable(), level: z.string() });
const presentation = z.object({ primary: action, alternatives: z.array(action), keyShare: z.number().nullable(), isKey: z.boolean(), weeklySpend: z.number(),
  restrictions: z.array(z.object({ code: z.string(), label: z.string() })), diagnostics: z.array(z.object({ code: z.string(), label: z.string(), basis: z.string() })), warning: z.string().nullable(),
  control: z.object({ status: z.string(), bidType: z.string().nullable(), paymentType: z.string().nullable(), excluded: z.boolean().nullable(), currentBid: z.number().nullable(), minimumBid: z.number().nullable(), bidStep: z.number().nullable(), receivedAtUtc: z.string().nullable() }).nullish() });
const decision = z.object({ status: z.string(), snoozeUntil: z.string().nullable(), reason: z.string().nullable(), comment: z.string().nullable(), createdAtUtc: z.string() });
const cardSchema = z.object({ evaluationId: z.string().uuid(), subject, rule, facts, norm, economy, preliminary: z.boolean(),
  presentation: presentation.nullish(), decision: decision.nullish() });
export type RecommendationCard = z.infer<typeof cardSchema>;
const evaluation = z.object({ evaluationId: z.string().uuid(), subject, facts, norm, economy, rules: z.array(rule),
  presentation: presentation.nullish(), quality: z.object({ fullStatsComplete: z.boolean(), clustersComplete: z.boolean(), fieldsVerified: z.boolean(),
    sourcesComparable: z.boolean(), maturity: z.string(), complete: z.boolean() }) });
const schema = z.object({ enabled: z.boolean(), isWbConnected: z.boolean(), runId: z.string().uuid().nullable(), schemaVersion: z.number(),
  window: z.object({ startDate: z.string(), endDate: z.string(), days: z.number(), isValid: z.boolean() }),
  calculatedAtUtc: z.string().nullable(), status: z.string(), message: z.string(),
  cards: z.array(cardSchema),
  packages: z.array(z.object({ id: z.string(), campaignId: z.string(), evaluationIds: z.array(z.string()), weeklySpend: z.number() })).nullish(),
  evaluations: z.array(evaluation), rows: z.array(wbClusterRowSchema),
  coverage: z.object({ enabled: z.number(), evaluated: z.number(), triggered: z.number(), skipped: z.number() }) });
export type ClusterRecommendationResponse = z.infer<typeof schema>;

export async function getClusterRecommendations(campaignId: string, filters: DashboardFilters) {
  const query = new URLSearchParams({ startDate: filters.startDate ?? '', endDate: filters.endDate ?? '' });
  return schema.parse(await httpClient<unknown>(`/api/wb/campaigns/${encodeURIComponent(campaignId)}/recommendations?${query}`));
}

const articleNorm = z.object({ articleId: z.string().uuid(), nomenclatureId: z.string(), name: z.string(),
  targetDrr: z.number().nullable(), effectiveTargetDrr: z.number(), source: z.string(), version: z.number(), launchDate: z.string().nullable(), effectiveLaunchDate: z.string().nullable() });
export type ArticleNorm = z.infer<typeof articleNorm>;
export async function getArticleNorms(campaignId: string) {
  return articleNorm.array().parse(await httpClient<unknown>(`/api/wb/norms/campaigns/${encodeURIComponent(campaignId)}/articles`));
}
export async function saveArticleNorm(campaignId: string, articleId: string, targetDrr: number | null, launchDate: string | null) {
  return httpClient<unknown>(`/api/wb/norms/campaigns/${encodeURIComponent(campaignId)}/articles/${encodeURIComponent(articleId)}`,
    { method: 'PUT', body: { targetDrr, launchDate } });
}

export async function saveSnapshotDecision(campaignId: string, runId: string, evaluationIds: string[], ruleId: string,
  status: string, snoozeUntil: string | null, reason: string | null, comment: string | null) {
  return schema.parse(await httpClient<unknown>(`/api/wb/campaigns/${campaignId}/recommendations/runs/${runId}/decisions`,
    { method: 'POST', body: { evaluationIds, ruleId, status, snoozeUntil, reason, comment } }));
}
const summary = z.object({ enabled: z.boolean(), schemaVersion: z.number(), top: z.array(z.object({ runId: z.string(), card: cardSchema })), remaining: z.number(),
  campaigns: z.array(z.object({ campaignId: z.string().nullable(), name: z.string(), runId: z.string().nullable(), status: z.string(), message: z.string(), count: z.number(), weeklySpend: z.number() })) });
export async function getRecommendationSummary(filters: DashboardFilters, storeId?: string) {
  const query = new URLSearchParams({ startDate: filters.startDate ?? '', endDate: filters.endDate ?? '' });
  if (storeId) query.set('storeId', storeId);
  return summary.parse(await httpClient<unknown>(`/api/wb/recommendations/summary?${query}`));
}
