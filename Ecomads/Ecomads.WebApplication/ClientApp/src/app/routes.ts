export const appRoutes = {
  root: '/',
  login: '/login',
  demoFeedback: '/demo-feedback',
  dashboard: '/dashboard',
  report: '/report',
  campaign: '/campaign/:campaignId',
  campaignPath: (campaignId: string) => `/campaign/${encodeURIComponent(campaignId)}`
} as const;
