import { httpClient } from '../../shared/api/httpClient';
import { recommendationStatsResponseSchema } from '../../shared/api/apiSchemas';
import type { RecommendationStatsResponse } from '../../shared/api/apiTypes';

export type ReportPeriod = 'week' | 'month' | 'quarter' | 'year';

export const reportPeriods: Array<{ value: ReportPeriod; label: string }> = [
  { value: 'week', label: 'Последние 7 дней' },
  { value: 'month', label: 'Последние 30 дней' },
  { value: 'quarter', label: 'Последние 90 дней' },
  { value: 'year', label: 'За год' }
];

export async function getRecommendationStats(period: ReportPeriod): Promise<RecommendationStatsResponse> {
  const response = await httpClient<unknown>(`/api/recommendations/stats?period=${period}`);
  return recommendationStatsResponseSchema.parse(response);
}

