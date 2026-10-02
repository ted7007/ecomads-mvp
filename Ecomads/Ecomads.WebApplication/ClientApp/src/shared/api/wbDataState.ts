import { z } from 'zod';
import { httpClient } from './httpClient';

const sourceSchema = z.object({
  kind: z.enum(['fullstats', 'orders', 'clusters', 'jam']),
  status: z.enum(['waiting', 'loading', 'failed', 'not_loaded', 'partial', 'complete']),
  startDate: z.string(), endDate: z.string(), covered: z.number().int(), expected: z.number().int(),
  availableStartDate: z.string().nullable(), availableEndDate: z.string().nullable(),
  lastCheckedAtUtc: z.string().nullable(), nextAttemptAtUtc: z.string().nullable(),
  estimatedCompletionAtUtc: z.string().nullable(), errorCode: z.string().nullable(), version: z.string()
});
const responseSchema = z.object({ sources: z.array(sourceSchema), active: z.boolean() });
export type WbDataState = z.infer<typeof responseSchema>;

export async function getWbDataState(startDate: string, endDate: string, campaignId?: string): Promise<WbDataState> {
  const query = new URLSearchParams({ startDate, endDate });
  if (campaignId) query.set('campaignId', campaignId);
  return responseSchema.parse(await httpClient<unknown>(`/api/wb/data-state?${query}`));
}
