import { z } from 'zod';
import { httpClient } from '../../shared/api/httpClient';

const valuesSchema = z.object({ targetDrr: z.number(), minClicks: z.number().int(), minSpend: z.number(),
  minOrders: z.number().int(), deviationPercent: z.number(), minCtr: z.number() });
const storeNormSchema = z.object({ storeId: z.string().uuid(), values: valuesSchema,
  version: z.number().int(), updatedAtUtc: z.string().nullable() });
const overridesSchema = z.object({ customName: z.string().nullable(), goal: z.string().nullable(),
  targetDrr: z.number().nullable(), minClicks: z.number().int().nullable(),
  minSpend: z.number().nullable(), minOrders: z.number().int().nullable(), deviationPercent: z.number().nullable(),
  minCtr: z.number().nullable() });
const campaignNormSchema = z.object({ campaignId: z.string().uuid(), overrides: overridesSchema,
  effective: valuesSchema, version: z.number().int(), storeVersion: z.number().int(),
  updatedAtUtc: z.string().nullable() });
const campaignSchema = z.object({ id: z.string().uuid(), name: z.string(), wbCampaignId: z.string(),
  wbStatus: z.number().int().nullable() });

export type StoreNormValues = z.infer<typeof valuesSchema>;
export type CampaignNormOverrides = z.infer<typeof overridesSchema>;
export type WbCampaignListItem = z.infer<typeof campaignSchema>;
const campaignNormListSchema = z.object({ campaignId: z.string().uuid(), wbName: z.string(),
  name: z.string(), goal: z.string().nullable(), targetDrr: z.number(), isInherited: z.boolean() });
export type CampaignNormListRow = z.infer<typeof campaignNormListSchema>;

export async function getCampaignNormList(storeId: string): Promise<CampaignNormListRow[]> {
  return campaignNormListSchema.array().parse(await httpClient<unknown>(
    `/api/wb/norms/stores/${encodeURIComponent(storeId)}/campaigns`));
}

export async function getStoreNorms(storeId: string) {
  return storeNormSchema.parse(await httpClient<unknown>(`/api/wb/norms/stores/${encodeURIComponent(storeId)}`));
}
export async function saveStoreNorms(storeId: string, values: StoreNormValues) {
  return storeNormSchema.parse(await httpClient<unknown>(`/api/wb/norms/stores/${encodeURIComponent(storeId)}`,
    { method: 'PUT', body: values }));
}
export async function getCampaignNorms(campaignId: string) {
  return campaignNormSchema.parse(await httpClient<unknown>(`/api/wb/norms/campaigns/${encodeURIComponent(campaignId)}`));
}
export async function saveCampaignNorms(campaignId: string, overrides: CampaignNormOverrides) {
  return campaignNormSchema.parse(await httpClient<unknown>(`/api/wb/norms/campaigns/${encodeURIComponent(campaignId)}`,
    { method: 'PUT', body: overrides }));
}
export async function getStoreCampaigns(storeId: string): Promise<WbCampaignListItem[]> {
  return campaignSchema.array().parse(await httpClient<unknown>(`/api/wb/stores/${encodeURIComponent(storeId)}/campaigns`));
}
