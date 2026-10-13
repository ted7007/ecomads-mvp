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
  threshold: z.number().nullable(), actionKind: z.literal('review'), tier: z.string() });
const evaluation = z.object({ evaluationId: z.string().uuid(), subject, facts, norm, economy, rules: z.array(rule),
  quality: z.object({ fullStatsComplete: z.boolean(), clustersComplete: z.boolean(), fieldsVerified: z.boolean(),
    sourcesComparable: z.boolean(), maturity: z.string(), complete: z.boolean() }) });
const schema = z.object({ enabled: z.boolean(), isWbConnected: z.boolean(), runId: z.string().uuid().nullable(), schemaVersion: z.number(),
  window: z.object({ startDate: z.string(), endDate: z.string(), days: z.number(), isValid: z.boolean() }),
  calculatedAtUtc: z.string().nullable(), status: z.string(), message: z.string(),
  cards: z.array(z.object({ evaluationId: z.string().uuid(), subject, rule, facts, norm, economy, preliminary: z.boolean() })),
  evaluations: z.array(evaluation), rows: z.array(wbClusterRowSchema),
  coverage: z.object({ enabled: z.number(), evaluated: z.number(), triggered: z.number(), skipped: z.number() }) });
export type ClusterRecommendationResponse = z.infer<typeof schema>;

export async function getClusterRecommendations(campaignId: string, filters: DashboardFilters) {
  const query = new URLSearchParams({ startDate: filters.startDate ?? '', endDate: filters.endDate ?? '' });
  return schema.parse(await httpClient<unknown>(`/api/wb/campaigns/${encodeURIComponent(campaignId)}/recommendations?${query}`));
}

const articleNorm = z.object({ articleId: z.string().uuid(), nomenclatureId: z.string(), name: z.string(),
  targetDrr: z.number().nullable(), effectiveTargetDrr: z.number(), source: z.string(), version: z.number() });
export type ArticleNorm = z.infer<typeof articleNorm>;
export async function getArticleNorms(campaignId: string) {
  return articleNorm.array().parse(await httpClient<unknown>(`/api/wb/norms/campaigns/${encodeURIComponent(campaignId)}/articles`));
}
export async function saveArticleNorm(campaignId: string, articleId: string, targetDrr: number | null) {
  return httpClient<unknown>(`/api/wb/norms/campaigns/${encodeURIComponent(campaignId)}/articles/${encodeURIComponent(articleId)}`,
    { method: 'PUT', body: { targetDrr } });
}
