export const appRoutes = {
  root: '/',
  login: '/login',
  dashboard: '/dashboard',
  report: '/report',
  campaign: '/campaign/:campaignId',
  campaignPath: (campaignId: string) => `/campaign/${encodeURIComponent(campaignId)}`
} as const;

export const legacyRoutes = {
  login: '/index.html',
  dashboard: '/dashboard.html',
  report: '/report.html',
  campaign: (campaignId: string) => `/campaign.html?id=${encodeURIComponent(campaignId)}`
} as const;

