import { z } from 'zod';
import { httpClient } from './httpClient';

const sourceSchema = z.object({
  kind: z.enum(['fullstats', 'expenses', 'orders', 'clusters', 'jam']),
  status: z.enum(['waiting', 'loading', 'failed', 'not_loaded', 'partial', 'complete']),
  startDate: z.string(), endDate: z.string(), covered: z.number().int(), expected: z.number().int(),
  noDataCampaigns: z.number().int().default(0),
  waitReason: z.string().nullable().default(null),
  availableStartDate: z.string().nullable(), availableEndDate: z.string().nullable(),
  lastCheckedAtUtc: z.string().nullable(), nextAttemptAtUtc: z.string().nullable(),
  estimatedCompletionAtUtc: z.string().nullable(), errorCode: z.string().nullable(),
  failedJobKind: z.string().nullable(), version: z.string()
});
const qualitySchema = z.object({
  storeId: z.string(), source: z.string(), entity: z.string(), startDate: z.string(), endDate: z.string(),
  state: z.string(), reason: z.string().nullable(), lastAttemptAtUtc: z.string(), acceptedAtUtc: z.string().nullable(),
  snapshotId: z.string().nullable(), acceptedSnapshotId: z.string().nullable(),
  previousValue: z.number().nullable(), proposedValue: z.number().nullable()
});
const responseSchema = z.object({ sources: z.array(sourceSchema), quality: z.array(qualitySchema).default([]), isPreliminary: z.boolean(), active: z.boolean() });
export type WbDataState = z.infer<typeof responseSchema>;
export type WbDataQuality = z.infer<typeof qualitySchema>;

export async function acceptWbRecalculation(row: WbDataQuality): Promise<void> {
  await httpClient(`/api/wb/stores/${row.storeId}/ingestion/expenses/${row.startDate}/accept-recalculation`, {
    method: 'POST', body: { snapshotId: row.snapshotId }
  });
}

export async function getWbDataState(startDate: string, endDate: string, campaignId?: string): Promise<WbDataState> {
  const query = new URLSearchParams({ startDate, endDate });
  if (campaignId) query.set('campaignId', campaignId);
  return responseSchema.parse(await httpClient<unknown>(`/api/wb/data-state?${query}`));
}
