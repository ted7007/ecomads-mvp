import { z } from 'zod';
import { httpClient } from './httpClient';

const decisionSchema = z.object({ recommendationKey: z.string(),
  status: z.enum(['accepted', 'snoozed', 'irrelevant']), updatedAtUtc: z.string() });
export type DecisionStatus = z.infer<typeof decisionSchema>['status'] | 'open';

export async function getRecommendationDecisions(startDate: string, endDate: string) {
  const query = new URLSearchParams({ startDate, endDate });
  return decisionSchema.array().parse(await httpClient<unknown>(`/api/wb/recommendation-decisions?${query}`));
}

export async function saveRecommendationDecision(recommendationKey: string, startDate: string,
  endDate: string, status: DecisionStatus) {
  return httpClient<unknown>('/api/wb/recommendation-decisions', {
    method: 'PUT', body: { recommendationKey, startDate, endDate, status }
  });
}
