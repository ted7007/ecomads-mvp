import { httpClient, sendRequest } from '../../shared/api/httpClient';
import { loadedPeriodsResponseSchema, projectsResponseSchema } from '../../shared/api/apiSchemas';
import type { LoadedPeriod, ProjectDashboard } from '../../shared/api/apiTypes';
import type { DashboardFilters } from '../DashboardPage/dashboardApi';
import {
  insightDecisionUpdateSchema,
  keywordRecommendationOverlaySchema,
  type InsightDecisionUpdate,
  type KeywordRecommendationOverlay
} from './campaignSchemas';

export type InsightDecision = 'accept' | 'apply' | 'postpone' | 'reject';

export type UploadKeywordStatsRequest = {
  file: File;
  startDate: string;
  endDate: string;
  campaignId: string;
};

export type TrackKeywordRecommendationOpenedRequest = {
  campaignId: string;
  keywordId: string;
  insightId?: string | null;
  recommendationStatus?: string | null;
  actionType?: string | null;
  priorityScore?: number | null;
  source?: 'deterministic' | 'llm';
};

export async function getCampaignSummary(campaignId: string, filters: DashboardFilters = {}): Promise<ProjectDashboard | null> {
  const query = new URLSearchParams();
  query.set('source', 'campaign-summary');

  if (filters.startDate) {
    query.set('startDate', filters.startDate);
  }

  if (filters.endDate) {
    query.set('endDate', filters.endDate);
  }

  const suffix = query.toString() ? `?${query.toString()}` : '';
  const response = await httpClient<unknown>(`/api/projects${suffix}`);
  const campaigns = projectsResponseSchema.parse(response);
  const normalizedCampaignId = campaignId.toLowerCase();

  return campaigns.find((campaign) => campaign.id.toLowerCase() === normalizedCampaignId) ?? null;
}

export async function getCampaignPeriods(): Promise<LoadedPeriod[]> {
  const response = await httpClient<unknown>('/api/statistics/periods');
  return loadedPeriodsResponseSchema.parse(response);
}

export async function getKeywordOverlay(campaignId: string, filters: DashboardFilters = {}): Promise<KeywordRecommendationOverlay> {
  const query = new URLSearchParams();

  if (filters.startDate) {
    query.set('startDate', filters.startDate);
  }

  if (filters.endDate) {
    query.set('endDate', filters.endDate);
  }

  const suffix = query.toString() ? `?${query.toString()}` : '';
  const response = await httpClient<unknown>(`/api/recommendations/campaign/${campaignId}/keyword-overlay${suffix}`);

  return keywordRecommendationOverlaySchema.parse(response);
}

export async function generateCampaignRecommendation(campaignId: string): Promise<void> {
  await httpClient<unknown>('/api/recommendations/generate', {
    method: 'POST',
    body: {
      campaignId,
      goal: 'рост прибыли'
    }
  });
}

export async function updateInsightDecision(insightId: string, decision: InsightDecision): Promise<InsightDecisionUpdate> {
  const response = await httpClient<unknown>(`/api/recommendations/insights/${encodeURIComponent(insightId)}/${decision}`, {
    method: 'POST'
  });

  return insightDecisionUpdateSchema.parse(response);
}

export async function updateInsightComment(insightId: string, userComment: string): Promise<InsightDecisionUpdate> {
  const response = await httpClient<unknown>(`/api/recommendations/insights/${encodeURIComponent(insightId)}/comment`, {
    method: 'PUT',
    body: { userComment }
  });

  return insightDecisionUpdateSchema.parse(response);
}

export async function uploadKeywordStats(request: UploadKeywordStatsRequest): Promise<void> {
  const formData = new FormData();
  formData.append('file', request.file);
  formData.append('startDate', request.startDate);
  formData.append('endDate', request.endDate);
  formData.append('campaignId', request.campaignId);

  await sendRequest('/api/statistics/upload-keywords', {
    method: 'POST',
    body: formData
  });
}

export async function trackKeywordRecommendationOpened(request: TrackKeywordRecommendationOpenedRequest): Promise<void> {
  await httpClient<unknown>('/api/product-analytics/events', {
    method: 'POST',
    body: {
      eventName: 'keyword_recommendation_opened',
      featureName: 'keyword_recommendations',
      campaignId: request.campaignId,
      keywordId: request.keywordId,
      insightId: request.insightId,
      recommendationStatus: request.recommendationStatus,
      actionType: request.actionType,
      priorityScore: request.priorityScore,
      source: request.source
    }
  });
}
