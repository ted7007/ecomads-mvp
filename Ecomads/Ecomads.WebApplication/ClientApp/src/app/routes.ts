export const appRoutes = {
  root: '/',
  login: '/login',
  demoFeedback: '/demo-feedback',
  dashboard: '/dashboard',
  wbStores: '/wb-stores',
  norms: '/norms',
  campaign: '/campaign/:campaignId',
  campaignPath: (campaignId: string) => `/campaign/${encodeURIComponent(campaignId)}`
} as const;
