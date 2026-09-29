import { httpClient } from '../../shared/api/httpClient';
import { loadedPeriodsResponseSchema, projectsResponseSchema } from '../../shared/api/apiSchemas';
import type { LoadedPeriod, ProjectDashboard } from '../../shared/api/apiTypes';

export type DashboardFilters = {
  startDate?: string;
  endDate?: string;
};

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
