import type { z } from 'zod';
import type {
  keywordStatSchema,
  loadedPeriodSchema,
  monthlyRecommendationStatsSchema,
  projectDashboardSchema,
  projectKpiSchema,
  recommendationCountsSchema,
  recommendationDetailSchema,
  recommendationStatsResponseSchema
} from './apiSchemas';

export type ProjectKpi = z.infer<typeof projectKpiSchema>;
export type ProjectDashboard = z.infer<typeof projectDashboardSchema>;
export type LoadedPeriod = z.infer<typeof loadedPeriodSchema>;
export type KeywordStat = z.infer<typeof keywordStatSchema>;
export type RecommendationCounts = z.infer<typeof recommendationCountsSchema>;
export type MonthlyRecommendationStats = z.infer<typeof monthlyRecommendationStatsSchema>;
export type RecommendationDetail = z.infer<typeof recommendationDetailSchema>;
export type RecommendationStatsResponse = z.infer<typeof recommendationStatsResponseSchema>;

