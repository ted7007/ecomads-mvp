import { z } from 'zod';

const nullableNumber = z.coerce.number().nullish();

const keywordStatuses = ['ToRemove', 'NeedsAttention', 'Effective', 'Watch', 'LowData', 'Neutral'] as const;
const priorityLevels = ['Low', 'Medium', 'High', 'Critical'] as const;
const confidenceLevels = ['Low', 'Medium', 'High'] as const;
const expectedEffectTypes = ['Saving', 'AdditionalRevenue', 'RiskReduction', 'NotCalculated'] as const;
const decisionStatuses = ['None', 'Accepted', 'Postponed', 'Rejected', 'Applied'] as const;
const recommendationActions = [
  'Watch',
  'CollectMoreData',
  'DecreaseBid',
  'DecreaseBidCarefully',
  'IncreaseBid',
  'IncreaseBidGradually',
  'IncreaseBidAggressively',
  'ConsiderMinusKeyword',
  'MinusKeyword',
  'ImmediateMinusKeyword',
  'MoveToWatchlist',
  'Optimize',
  'Scale',
  'AggressiveScale',
  'FindSimilarKeywords',
  'Maintain',
  'Disable',
  'ImmediateDisable',
  'AggressiveBidChange',
  'SeparateControl',
  'ScaleGoodKeywords',
  'IncreaseBidForScaleCandidates',
  'ExpandRelevantKeywords',
  'AcceptHigherDrrTemporarily',
  'AggressivelyReduceAllSpend',
  'DisableConvertingKeywords'
] as const;

function enumValueSchema<TValue extends readonly [string, ...string[]]>(values: TValue, fallback: TValue[number]) {
  return z.preprocess((value) => {
    if (typeof value === 'number' && Number.isInteger(value) && values[value]) {
      return values[value];
    }

    if (typeof value === 'string' && values.includes(value)) {
      return value;
    }

    return fallback;
  }, z.enum(values).default(fallback));
}

const keywordStatusSchema = enumValueSchema(keywordStatuses, 'Neutral');
const priorityLevelSchema = enumValueSchema(priorityLevels, 'Low');
const confidenceLevelSchema = enumValueSchema(confidenceLevels, 'Low');
const expectedEffectTypeSchema = enumValueSchema(expectedEffectTypes, 'NotCalculated');
const decisionStatusSchema = enumValueSchema(decisionStatuses, 'None');
const recommendationActionSchema = enumValueSchema(recommendationActions, 'Watch');

export const recommendationStatusCountsSchema = z.object({
  toRemove: z.coerce.number().default(0),
  needsAttention: z.coerce.number().default(0),
  effective: z.coerce.number().default(0),
  watch: z.coerce.number().default(0),
  lowData: z.coerce.number().default(0),
  neutral: z.coerce.number().default(0)
});

export const keywordRecommendationSummarySchema = z.object({
  earned: nullableNumber,
  spend: nullableNumber,
  orders: z.coerce.number().default(0),
  drr: nullableNumber,
  ctr: nullableNumber
});

export const recommendationOverlaySummarySchema = z.object({
  text: z.string().default(''),
  generatedWithoutLlm: z.boolean().default(false),
  counts: recommendationStatusCountsSchema.default({})
});

export const keywordRecommendationRowSchema = z.object({
  keywordId: z.string(),
  phrase: z.string(),
  views: z.coerce.number().nullish(),
  clicks: z.coerce.number().nullish(),
  ctr: nullableNumber,
  spend: nullableNumber,
  orders: z.coerce.number().nullish(),
  revenue: nullableNumber,
  drr: nullableNumber,
  status: keywordStatusSchema,
  priorityScore: z.coerce.number().default(0),
  priorityLevel: priorityLevelSchema,
  confidenceLevel: confidenceLevelSchema,
  shortRecommendation: z.string().nullish(),
  recommendedAction: recommendationActionSchema.nullish(),
  expectedEffectType: expectedEffectTypeSchema,
  expectedEffectMoney: nullableNumber,
  expectedEffectText: z.string().default(''),
  mainInsightId: z.string().nullish(),
  hasInsight: z.boolean().default(false),
  decisionStatus: decisionStatusSchema
});

export const insightHistoryItemSchema = z.object({
  type: z.string(),
  createdAt: z.string(),
  comment: z.string().nullish()
});

export const keywordInsightDetailSchema = z.object({
  insightId: z.string(),
  keywordId: z.string(),
  phrase: z.string(),
  status: keywordStatusSchema,
  priorityScore: z.coerce.number().default(0),
  priorityLevel: priorityLevelSchema,
  confidenceLevel: confidenceLevelSchema,
  metrics: z.record(z.coerce.number().nullish()).default({}),
  reasonCodes: z.array(z.string()).default([]),
  shortExplanation: z.string().default(''),
  expectedEffectType: expectedEffectTypeSchema,
  expectedEffectMoney: nullableNumber,
  expectedEffectText: z.string().default(''),
  recommendedActionTitle: z.string().default(''),
  recommendedActionDescription: z.string().default(''),
  allowedActions: z.array(recommendationActionSchema).default([]),
  forbiddenActions: z.array(recommendationActionSchema).default([]),
  decisionStatus: decisionStatusSchema,
  userComment: z.string().nullish(),
  history: z.array(insightHistoryItemSchema).default([])
});

export const campaignRecommendationInsightSchema = z.object({
  insightId: z.string(),
  type: z.string(),
  priorityScore: z.coerce.number().default(0),
  priorityLevel: priorityLevelSchema,
  text: z.string().default(''),
  expectedEffectType: expectedEffectTypeSchema,
  expectedEffectMoney: nullableNumber,
  expectedEffectText: z.string().default(''),
  decisionStatus: decisionStatusSchema,
  userComment: z.string().nullish(),
  history: z.array(insightHistoryItemSchema).default([])
});

export const keywordRecommendationOverlaySchema = z.object({
  campaignId: z.string(),
  generatedAt: z.string().nullish(),
  summary: keywordRecommendationSummarySchema.default({}),
  recommendationSummary: recommendationOverlaySummarySchema.default({}),
  keywords: z.array(keywordRecommendationRowSchema).default([]),
  insights: z.array(keywordInsightDetailSchema).default([]),
  campaignInsights: z.array(campaignRecommendationInsightSchema).default([])
});

export const insightDecisionUpdateSchema = z.object({
  recommendationId: z.string(),
  insightId: z.string(),
  decisionStatus: decisionStatusSchema,
  userComment: z.string().nullish(),
  updatedAt: z.string(),
  history: z.array(insightHistoryItemSchema).default([])
});

export type KeywordRecommendationOverlay = z.infer<typeof keywordRecommendationOverlaySchema>;
export type KeywordRecommendationRow = z.infer<typeof keywordRecommendationRowSchema>;
export type KeywordInsightDetail = z.infer<typeof keywordInsightDetailSchema>;
export type InsightDecisionUpdate = z.infer<typeof insightDecisionUpdateSchema>;
