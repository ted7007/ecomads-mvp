import { z } from 'zod';
import { httpClient } from '../../shared/api/httpClient';

const storeSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  externalId: z.string(),
  tokenLastFour: z.string(),
  tokenExpiresAtUtc: z.string(),
  lastSyncAt: z.string().nullable(),
  campaignCount: z.number().int(),
  jamStatus: z.enum(['unknown', 'active', 'access_denied', 'payment_required']),
  jamCheckedAtUtc: z.string().nullable()
});

export type WbStore = z.infer<typeof storeSchema>;

const syncSchema = z.object({
  id: z.string().uuid(),
  kind: z.enum(['fullstats', 'funnel', 'clusters', 'jam']),
  status: z.enum(['pending', 'running', 'completed', 'failed']),
  startDate: z.string(),
  endDate: z.string(),
  processedCampaigns: z.number().int(),
  totalCampaigns: z.number().int(),
  nextAttemptAtUtc: z.string(),
  errorCode: z.string().nullable()
});

export type WbSync = z.infer<typeof syncSchema>;

const jobSchema = z.object({
  id: z.string().uuid(), kind: z.enum(['fullstats', 'funnel', 'clusters', 'jam']),
  status: z.enum(['pending', 'running', 'completed', 'failed']),
  stage: z.string(), waitReason: z.string().nullable(),
  startDate: z.string(), endDate: z.string(),
  processedCount: z.number().int(), totalCount: z.number().int(),
  unit: z.enum(['campaign', 'pair', 'product', 'request']),
  createdAtUtc: z.string(), startedAtUtc: z.string().nullable(),
  updatedAtUtc: z.string(), completedAtUtc: z.string().nullable(),
  nextAttemptAtUtc: z.string(), errorCode: z.string().nullable(),
  retriedFromJobId: z.string().uuid().nullable(), canRetry: z.boolean()
});
export type WbSyncJob = z.infer<typeof jobSchema>;
const overviewSchema = z.object({
  sources: z.array(z.object({ kind: jobSchema.shape.kind,
    lastJob: jobSchema.nullable(), lastSuccessAtUtc: z.string().nullable() })),
  activeJob: jobSchema.nullable(), blockedReason: z.string().nullable()
});
export type WbSyncOverview = z.infer<typeof overviewSchema>;
const historySchema = z.object({ items: z.array(jobSchema), total: z.number().int(),
  page: z.number().int(), pageSize: z.number().int() });
const eventSchema = z.object({ occurredAtUtc: z.string(), stage: z.string(), errorCode: z.string().nullable(),
  processedCount: z.number().int(), attemptNumber: z.number().int() });
const detailsSchema = z.object({ job: jobSchema, campaignIds: z.array(z.number()), events: z.array(eventSchema) });

export async function getWbSyncOverview(storeId: string): Promise<WbSyncOverview> {
  return overviewSchema.parse(await httpClient<unknown>(`/api/wb/stores/${encodeURIComponent(storeId)}/sync-overview`));
}
export async function getWbSyncHistory(storeId: string, page: number, kind: string, status: string) {
  const query = new URLSearchParams({ page: String(page), pageSize: '10' });
  if (kind) query.set('kind', kind);
  if (status) query.set('status', status);
  return historySchema.parse(await httpClient<unknown>(`/api/wb/stores/${encodeURIComponent(storeId)}/sync-jobs?${query}`));
}
export async function getWbSyncDetails(storeId: string, jobId: string) {
  return detailsSchema.parse(await httpClient<unknown>(`/api/wb/stores/${encodeURIComponent(storeId)}/sync-jobs/${encodeURIComponent(jobId)}`));
}
export async function retryWbSync(storeId: string, jobId: string) {
  return jobSchema.parse(await httpClient<unknown>(`/api/wb/stores/${encodeURIComponent(storeId)}/sync-jobs/${encodeURIComponent(jobId)}/retry`, { method: 'POST' }));
}

export async function getWbStores(): Promise<WbStore[]> {
  return z.array(storeSchema).parse(await httpClient<unknown>('/api/wb/stores'));
}

export async function connectWbStore(token: string): Promise<WbStore> {
  return storeSchema.parse(await httpClient<unknown>('/api/wb/stores/connect', {
    method: 'POST',
    body: { token }
  }));
}

export async function disconnectWbStore(storeId: string): Promise<void> {
  await httpClient<void>(`/api/wb/stores/${encodeURIComponent(storeId)}`, { method: 'DELETE' });
}

export async function getWbSync(storeId: string): Promise<WbSync | undefined> {
  const response = await httpClient<unknown>(`/api/wb/stores/${encodeURIComponent(storeId)}/sync`);
  return response === undefined ? undefined : syncSchema.parse(response);
}

export async function startWbSync(storeId: string, request: { startDate?: string; endDate?: string; campaignIds?: number[] }): Promise<WbSync> {
  return syncSchema.parse(await httpClient<unknown>(`/api/wb/stores/${encodeURIComponent(storeId)}/sync`, {
    method: 'POST',
    body: request
  }));
}

export async function startWbClusterSync(storeId: string, request: { startDate?: string; endDate?: string; campaignIds?: number[] }): Promise<WbSync> {
  return syncSchema.parse(await httpClient<unknown>(`/api/wb/stores/${encodeURIComponent(storeId)}/clusters/sync`, {
    method: 'POST',
    body: request
  }));
}

export async function startWbJamSync(storeId: string, request: { startDate?: string; endDate?: string; campaignIds?: number[] }): Promise<WbSync> {
  return syncSchema.parse(await httpClient<unknown>(`/api/wb/stores/${encodeURIComponent(storeId)}/jam/sync`, {
    method: 'POST', body: request
  }));
}
