import { z } from 'zod';
import { httpClient } from './httpClient';
import { wbClusterRowSchema } from '../../pages/CampaignPage/campaignApi';


const facts = z.object({ spend: z.number(), views: z.number().nullable(), clicks: z.number().nullable(),
  carts: z.number().nullable(), orders: z.number().nullable(), orderedProducts: z.number().nullable(), unitPrice: z.number().nullable() });
// "article" and articleVersion remain readable in immutable historical snapshots only.
const norm = z.object({ value: z.number(), source: z.enum(['article', 'campaign', 'store', 'default']),
  storeVersion: z.number(), campaignVersion: z.number(), articleVersion: z.number() });
const economy = z.object({ estimatedOrderSum: z.number().nullable(), drr: z.number().nullable(),
  drrMin: z.number().nullable(), drrMax: z.number().nullable(), drrPlusOne: z.number().nullable(), allowedSpendPerUnit: z.number().nullable() });
const subject = z.object({ storeId: z.string().uuid(), campaignId: z.string().uuid(), articleId: z.string().uuid().nullable(),
  nomenclatureId: z.string().nullable(), nomenclatureName: z.string().nullable(), clusterName: z.string().nullable(), scope: z.enum(['campaign','article','cluster']).default('cluster') });
const rule = z.object({ ruleId: z.string(), ruleVersion: z.number(), group: z.number(), enabled: z.boolean(),
  status: z.enum(['triggered', 'not_triggered', 'skipped', 'disabled']), reasonCode: z.string(), explanation: z.string(),
  threshold: z.number().nullable(), actionKind: z.string(), tier: z.string() });
const action = z.object({ kind: z.string(), label: z.string(), reductionPercent: z.number().nullable(), targetBid: z.number().nullable(), level: z.string() });
const operationalDetails = z.object({ kind: z.string(), window: z.object({ startDate: z.string(), endDate: z.string() }),
  sources: z.array(z.object({ snapshotId: z.string().nullable(), source: z.string(), state: z.string(), requestedAtUtc: z.string().nullable(), receivedAtUtc: z.string().nullable(), errorCode: z.string().nullable() })),
  fbw: z.number().nullable(), fbs: z.number().nullable(), stockDays: z.number().nullable(), dailySpend: z.number().nullable(), dailyUnits: z.number().nullable(),
  budget: z.number().nullable(), budgetDays: z.number().nullable(), topUp: z.number().nullable(), balance: z.number().nullable(), affectedArticles: z.array(z.string()).nullable(), blockGrowth: z.boolean() });
const presentation = z.object({ primary: action, alternatives: z.array(action), keyShare: z.number().nullable(), isKey: z.boolean(), weeklySpend: z.number(), details: operationalDetails.nullish(), priority: z.number().optional(),
  clickEvidence: z.object({ cr: z.number(), source: z.enum(['actual', 'fallback']),
    window: z.object({ startDate: z.string(), endDate: z.string() }), minimumClicks: z.number().nullable(),
    result: z.enum(['unknown', 'sufficient', 'insufficient']) }).nullish(),
  restrictions: z.array(z.object({ code: z.string(), label: z.string() })), diagnostics: z.array(z.object({ code: z.string(), label: z.string(), basis: z.string() })), warning: z.string().nullable(),
  control: z.object({ status: z.string(), bidType: z.string().nullable(), paymentType: z.string().nullable(), excluded: z.boolean().nullable(), currentBid: z.number().nullable(), minimumBid: z.number().nullable(), bidStep: z.number().nullable(), receivedAtUtc: z.string().nullable() }).nullish() });
const decision = z.object({ status: z.string(), snoozeUntil: z.string().nullable(), reason: z.string().nullable(), comment: z.string().nullable(), createdAtUtc: z.string() });
const cardSchema = z.object({ evaluationId: z.string().uuid(), subject, rule, facts, norm, economy, preliminary: z.boolean(),
  presentation: presentation.nullish(), decision: decision.nullish(),
  ranking: z.object({ version: z.number(), queue: z.enum(['urgent', 'actionable', 'review']),
    weeklyInefficientSpend: z.number().nullable(), basis: z.string() }).nullish() });
export type RecommendationCard = z.infer<typeof cardSchema>;
const evaluation = z.object({ evaluationId: z.string().uuid(), subject, facts, norm, economy, rules: z.array(rule),
  presentation: presentation.nullish(), quality: z.object({ fullStatsComplete: z.boolean(), clustersComplete: z.boolean(), fieldsVerified: z.boolean(),
    sourcesComparable: z.boolean(), maturity: z.string(), complete: z.boolean(), availableDays: z.number().nullish(), expectedDays: z.number().nullish() }) });
const schema = z.object({ enabled: z.boolean(), isWbConnected: z.boolean(), runId: z.string().uuid().nullable(), schemaVersion: z.number(),
  window: z.object({ startDate: z.string(), endDate: z.string(), days: z.number(), isValid: z.boolean() }),
  calculatedAtUtc: z.string().nullable(), status: z.string(), message: z.string(),
  cards: z.array(cardSchema),
  packages: z.array(z.object({ id: z.string(), campaignId: z.string(), evaluationIds: z.array(z.string()), weeklySpend: z.number() })).nullish(),
  evaluations: z.array(evaluation), rows: z.array(wbClusterRowSchema),
  coverage: z.object({ enabled: z.number(), evaluated: z.number(), triggered: z.number(), skipped: z.number() }) });
export type ClusterRecommendationResponse = z.infer<typeof schema>;

export async function getClusterRecommendations(campaignId: string) {
  return schema.parse(await httpClient<unknown>(`/api/wb/campaigns/${encodeURIComponent(campaignId)}/recommendations`));
}

export async function saveSnapshotDecision(campaignId: string, runId: string, evaluationIds: string[], ruleId: string,
  status: string, snoozeUntil: string | null, reason: string | null, comment: string | null, reasonCode: string | null = null) {
  return schema.parse(await httpClient<unknown>(`/api/wb/campaigns/${campaignId}/recommendations/runs/${runId}/decisions`,
    { method: 'POST', body: { evaluationIds, ruleId, status, snoozeUntil, reason, comment, reasonCode } }));
}
const summary = z.object({ window: z.object({ startDate: z.string(), endDate: z.string() }), enabled: z.boolean(), schemaVersion: z.number(), top: z.array(z.object({ runId: z.string(), card: cardSchema })), remaining: z.number(),
  campaigns: z.array(z.object({ campaignId: z.string().nullable(), name: z.string(), runId: z.string().nullable(), status: z.string(), message: z.string(), count: z.number(), weeklySpend: z.number() })) });
export async function getRecommendationSummary(storeId?: string) {
  const query = new URLSearchParams();
  if (storeId) query.set('storeId', storeId);
  return summary.parse(await httpClient<unknown>(`/api/wb/recommendations/summary?${query}`));
}
