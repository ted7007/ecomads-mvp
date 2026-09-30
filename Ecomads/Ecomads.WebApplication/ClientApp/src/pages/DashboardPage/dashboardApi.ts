import { httpClient } from '../../shared/api/httpClient';
import { z } from 'zod';
import { loadedPeriodsResponseSchema, projectsResponseSchema } from '../../shared/api/apiSchemas';
import type { LoadedPeriod, ProjectDashboard } from '../../shared/api/apiTypes';

export type DashboardFilters = {
  startDate?: string;
  endDate?: string;
};

const dailyPointSchema = z.object({
  date: z.string(), spend: z.number().nullable(), revenue: z.number().nullable(),
  clicks: z.number().int().nullable(), impressions: z.number().int().nullable(),
  orders: z.number().int().nullable(), drr: z.number().nullable(), ctr: z.number().nullable(),
  loadedCampaigns: z.number().int(), expectedCampaigns: z.number().int()
});
export type DailyPoint = z.infer<typeof dailyPointSchema>;

export async function getDailySeries(filters: DashboardFilters, campaignId?: string): Promise<DailyPoint[]> {
  const query = new URLSearchParams();
  if (filters.startDate) query.set('startDate', filters.startDate);
  if (filters.endDate) query.set('endDate', filters.endDate);
  if (campaignId) query.set('campaignId', campaignId);
  return dailyPointSchema.array().parse(await httpClient<unknown>(`/api/statistics/daily?${query}`));
}

export async function getCampaigns(filters: DashboardFilters = {}): Promise<ProjectDashboard[]> {
  const query = new URLSearchParams();

  if (filters.startDate) {
    query.set('startDate', filters.startDate);
  }

  if (filters.endDate) {
    query.set('endDate', filters.endDate);
  }

  const suffix = query.toString() ? `?${query.toString()}` : '';
  const response = await httpClient<unknown>(`/api/projects${suffix}`);

  return projectsResponseSchema.parse(response);
}

export async function getLoadedPeriods(): Promise<LoadedPeriod[]> {
  const response = await httpClient<unknown>('/api/statistics/periods');
  return loadedPeriodsResponseSchema.parse(response);
}
