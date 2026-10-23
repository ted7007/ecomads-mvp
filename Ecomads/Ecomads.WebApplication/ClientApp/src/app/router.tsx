import { createBrowserRouter, Navigate } from 'react-router-dom';
import { RequireAuth } from '../shared/auth/RequireAuth';
import { appRoutes } from './routes';

export const router = createBrowserRouter(
  [
    { path: appRoutes.site, lazy: async () => ({ Component: (await import('../pages/LandingPage/LandingPage')).LandingPage }) },
    {
      path: appRoutes.login,
      lazy: async () => ({ Component: (await import('../pages/LoginPage/LoginPage')).LoginPage })
    },
    {
      element: <RequireAuth />,
      children: [
        {
          lazy: async () => ({ Component: (await import('../shared/ui/AppLayout')).AppLayout }),
          children: [
            { path: appRoutes.pilotApplications, lazy: async () => ({ Component: (await import('../pages/PilotApplicationsPage/PilotApplicationsPage')).PilotApplicationsPage }) },
            {
              path: appRoutes.root,
              element: <Navigate to={appRoutes.dashboard} replace />
            },
            {
              path: appRoutes.dashboard,
              lazy: async () => ({ Component: (await import('../pages/DashboardPage/DashboardPage')).DashboardPage })
            },
            {
              path: appRoutes.wbStores,
              lazy: async () => ({ Component: (await import('../pages/WbStoresPage/WbStoresPage')).WbStoresPage })
            },
            {
              path: appRoutes.norms,
              lazy: async () => ({ Component: (await import('../pages/NormsPage/NormsPage')).NormsPage })
            },
            { path: appRoutes.results, lazy: async () => ({ Component: (await import('../pages/ResultsPage/ResultsPage')).ResultsPage }) },
            {
              path: appRoutes.demoFeedback,
              lazy: async () => ({ Component: (await import('../pages/DemoFeedbackPage/DemoFeedbackPage')).DemoFeedbackPage })
            },
            {
              path: appRoutes.campaign,
              lazy: async () => ({ Component: (await import('../pages/CampaignPage/CampaignPage')).CampaignPage })
            }
          ]
        }
      ]
    },
    {
      path: '*',
      element: <Navigate to={appRoutes.dashboard} replace />
    }
  ]
);

