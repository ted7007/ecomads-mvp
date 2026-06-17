import { sendRequest, httpClient } from '../../shared/api/httpClient';
import { loadedPeriodsResponseSchema, projectsResponseSchema } from '../../shared/api/apiSchemas';
import type { LoadedPeriod, ProjectDashboard } from '../../shared/api/apiTypes';

export type DashboardFilters = {
  startDate?: string;
  endDate?: string;
};

export type DashboardUploadMode = 'general' | 'with-keywords';

export type UploadStatisticsRequest = {
  file: File;
  startDate: string;
  endDate: string;
  mode: DashboardUploadMode;
  keywordsFile?: File | null;
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
  formData.append('file', request.file);
  formData.append('startDate', request.startDate);
  formData.append('endDate', request.endDate);

  let endpoint = '/api/statistics/upload';

  if (request.mode === 'with-keywords') {
    if (!request.keywordsFile) {
      throw new Error('Добавьте файл отчета по ключевым словам.');
    }

    formData.append('keywordsFile', request.keywordsFile);
    endpoint = '/api/statistics/upload-with-keywords';
  }

  await sendRequest(endpoint, {
    method: 'POST',
    body: formData
  });
}
