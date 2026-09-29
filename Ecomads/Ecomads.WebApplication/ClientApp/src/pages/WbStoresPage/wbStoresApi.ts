import { z } from 'zod';
import { httpClient } from '../../shared/api/httpClient';

const storeSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  externalId: z.string(),
  tokenLastFour: z.string(),
  tokenExpiresAtUtc: z.string(),
  lastSyncAt: z.string().nullable(),
  campaignCount: z.number().int()
});

export type WbStore = z.infer<typeof storeSchema>;

const syncSchema = z.object({
  id: z.string().uuid(),
  kind: z.enum(['fullstats', 'clusters']),
  status: z.enum(['pending', 'running', 'completed', 'failed']),
  startDate: z.string(),
  endDate: z.string(),
  processedCampaigns: z.number().int(),
  totalCampaigns: z.number().int(),
  nextAttemptAtUtc: z.string(),
  errorCode: z.string().nullable()
});

export type WbSync = z.infer<typeof syncSchema>;

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
