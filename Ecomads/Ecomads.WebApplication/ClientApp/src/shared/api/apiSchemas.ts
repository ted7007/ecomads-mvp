import { z } from 'zod';

export const projectKpiSchema = z.object({
  spend: z.coerce.number(),
  revenue: z.coerce.number(),
  orderedAmount: z.coerce.number(),
  drr: z.coerce.number(),
  clicks: z.coerce.number(),
  ctr: z.coerce.number()
});

export const projectDashboardSchema = z.object({
  id: z.string(),
  name: z.string(),
  kpi: projectKpiSchema
});

export const projectsResponseSchema = z.array(projectDashboardSchema);

export const loadedPeriodSchema = z.object({
  startDate: z.string(),
  endDate: z.string()
});

export const loadedPeriodsResponseSchema = z.array(loadedPeriodSchema);

export const keywordStatSchema = z.object({
  phrase: z.string(),
  frequency: z.coerce.number().nullish(),
  cpm: z.coerce.number().nullish(),
  avgPosition: z.coerce.number().nullish(),
  impressions: z.coerce.number(),
  clicks: z.coerce.number(),
  ctr: z.coerce.number(),
  spend: z.coerce.number(),
  orders: z.coerce.number(),
  revenue: z.coerce.number(),
  drr: z.coerce.number()
});

export const keywordStatsResponseSchema = z.array(keywordStatSchema);

export const recommendationCountsSchema = z.object({
  accepted: z.coerce.number(),
  pending: z.coerce.number(),
  rejected: z.coerce.number().default(0),
  applied: z.coerce.number().default(0)
});

export const monthlyRecommendationStatsSchema = z.object({
  month: z.string(),
  accepted: z.coerce.number(),
  pending: z.coerce.number(),
  rejected: z.coerce.number().default(0),
  applied: z.coerce.number().default(0),
  total: z.coerce.number()
});

export const recommendationDetailSchema = z.object({
  id: z.string(),
  text: z.string().nullish(),
  entityName: z.string().nullish(),
  action: z.string().nullish(),
  status: z.string(),
  date: z.string(),
  campaign: z.string(),
  comment: z.string().nullish(),
  expectedEffectType: z.string().nullish(),
  expectedEffectMoney: z.coerce.number().nullish(),
  expectedEffectText: z.string().nullish(),
  actualEffectMoney: z.coerce.number().nullish(),
  actualEffectStatus: z.string().nullish(),
  actualEffectText: z.string().nullish(),
  period: z.string().nullish()
});

export const recommendationStatsResponseSchema = z.object({
  counts: recommendationCountsSchema,
  monthly: z.array(monthlyRecommendationStatsSchema).default([]),
  recommendations: z.array(recommendationDetailSchema).default([]),
  expectedSaving: z.coerce.number().default(0),
  expectedAdditionalRevenue: z.coerce.number().default(0),
  notCalculatedCount: z.coerce.number().default(0)
});
