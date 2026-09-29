import { z } from 'zod';
import { httpClient } from '../../shared/api/httpClient';
import { loadedPeriodsResponseSchema, projectsResponseSchema } from '../../shared/api/apiSchemas';
import type { DashboardFilters } from '../DashboardPage/dashboardApi';

const nomenclatureSchema = z.object({
  nomenclatureId: z.string(), name: z.string(), impressions: z.number(), clicks: z.number(),
  carts: z.number(), orders: z.number(), spend: z.number(), revenue: z.number(),
  ctr: z.number().nullable(), cr: z.number().nullable(), cpc: z.number().nullable(), cpo: z.number().nullable()
});
export type NomenclatureStatistics = z.infer<typeof nomenclatureSchema>;

const wbClusterRowSchema = z.object({
  nomenclatureId: z.string(), nomenclatureName: z.string(), clusterName: z.string(),
  spend: z.number(), views: z.number().int().nullable(), clicks: z.number().int().nullable(),
  carts: z.number().int().nullable(), orders: z.number().int().nullable(), cpc: z.number().nullable(),
  assessment: z.string()
});
const wbClustersSchema = z.object({ isWbConnected: z.boolean(), rows: z.array(wbClusterRowSchema),
  storeNormVersion: z.number().int(), campaignNormVersion: z.number().int() });
export type WbClusterRow = z.infer<typeof wbClusterRowSchema>;

function queryString(filters: DashboardFilters): string {
  const query = new URLSearchParams();
  if (filters.startDate) query.set('startDate', filters.startDate);
  if (filters.endDate) query.set('endDate', filters.endDate);
  return query.size ? `?${query.toString()}` : '';
}

export async function getCampaignSummary(campaignId: string, filters: DashboardFilters = {}) {
  const campaigns = projectsResponseSchema.parse(await httpClient<unknown>(`/api/projects${queryString(filters)}`));
  return campaigns.find((campaign) => campaign.id.toLowerCase() === campaignId.toLowerCase()) ?? null;
}

export async function getCampaignPeriods() {
  return loadedPeriodsResponseSchema.parse(await httpClient<unknown>('/api/statistics/periods'));
}

export async function getNomenclatureStatistics(campaignId: string, filters: DashboardFilters = {}) {
  return nomenclatureSchema.array().parse(await httpClient<unknown>(
    `/api/statistics/nomenclatures/${encodeURIComponent(campaignId)}${queryString(filters)}`));
}

export async function getWbClusters(campaignId: string, filters: DashboardFilters = {}) {
  return wbClustersSchema.parse(await httpClient<unknown>(
    `/api/wb/campaigns/${encodeURIComponent(campaignId)}/clusters${queryString(filters)}`));
}
