export const appRoutes = {
  root: '/',
  login: '/login',
  site: '/site',
  pilotApplications: '/admin/pilot-applications',
  demoFeedback: '/demo-feedback',
  dashboard: '/dashboard',
  wbStores: '/wb-stores',
  norms: '/norms',
  results: '/results',
  campaign: '/campaign/:campaignId',
  campaignPath: (campaignId: string) => `/campaign/${encodeURIComponent(campaignId)}`
} as const;
