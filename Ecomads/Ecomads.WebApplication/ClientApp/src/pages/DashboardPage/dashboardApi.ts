import { sendRequest, httpClient } from '../../shared/api/httpClient';
import { loadedPeriodsResponseSchema, projectsResponseSchema } from '../../shared/api/apiSchemas';
import type { LoadedPeriod, ProjectDashboard } from '../../shared/api/apiTypes';

export type DashboardFilters = {
  startDate?: string;
  endDate?: string;
};

export type UploadStatisticsRequest = {
  campaignNamesFile: File;
  wbStatisticsFiles: File[];
  evirmaFiles: File[];
  startDate: string;
  endDate: string;
};

export async function getCampaigns(filters: DashboardFilters = {}): Promise<ProjectDashboard[]> {
  const query = new URLSearchParams();
  query.set('source', 'dashboard');

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

export async function uploadDashboardStatistics(request: UploadStatisticsRequest): Promise<void> {
  const formData = new FormData();
  formData.append('campaignNamesFile', request.campaignNamesFile);
  request.wbStatisticsFiles.forEach((file) => formData.append('wbStatisticsFiles', file));
  request.evirmaFiles.forEach((file) => formData.append('evirmaFiles', file));
  formData.append('startDate', request.startDate);
  formData.append('endDate', request.endDate);
  await sendRequest('/api/statistics/import', {
    method: 'POST',
    body: formData
  });
}
