export const queryKeys = {
  auth: {
    me: ['auth', 'me'] as const
  },
  demoFeedback: {
    current: ['demo-feedback', 'current'] as const
  },
  projects: {
    list: (filters: { startDate?: string; endDate?: string } = {}) => ['projects', filters] as const
  },
  statistics: {
    periods: ['statistics', 'periods'] as const,
    keywords: (campaignId: string, filters: { startDate?: string; endDate?: string } = {}) =>
      ['statistics', 'keywords', campaignId, filters] as const
  },
  recommendations: {
    stats: (period: string) => ['recommendations', 'stats', period] as const,
    campaign: (campaignId: string) => ['recommendations', 'campaign', campaignId] as const,
    keywordOverlay: (campaignId: string, filters: { startDate?: string; endDate?: string } = {}) =>
      ['recommendations', 'keyword-overlay', campaignId, filters] as const
  }
};
