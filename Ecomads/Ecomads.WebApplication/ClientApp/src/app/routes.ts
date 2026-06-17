export const appRoutes = {
  root: '/',
  login: '/login',
  dashboard: '/dashboard',
  report: '/report',
  campaign: '/campaign/:campaignId',
  campaignPath: (campaignId: string) => `/campaign/${encodeURIComponent(campaignId)}`
} as const;
