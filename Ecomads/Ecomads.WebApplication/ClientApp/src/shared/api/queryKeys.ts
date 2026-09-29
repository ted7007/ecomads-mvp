export const queryKeys = {
  auth: { me: ['auth', 'me'] as const },
  demoFeedback: { current: ['demo-feedback', 'current'] as const },
  projects: {
    list: (filters: { startDate?: string; endDate?: string } = {}) => ['projects', filters] as const
  },
  statistics: { periods: ['statistics', 'periods'] as const }
};
