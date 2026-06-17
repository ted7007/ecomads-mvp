import { createBrowserRouter, Navigate } from 'react-router-dom';
import { CampaignPage } from '../pages/CampaignPage/CampaignPage';
import { DashboardPage } from '../pages/DashboardPage/DashboardPage';
import { DemoFeedbackPage } from '../pages/DemoFeedbackPage/DemoFeedbackPage';
import { LoginPage } from '../pages/LoginPage/LoginPage';
import { ReportPage } from '../pages/ReportPage/ReportPage';
import { RequireAuth } from '../shared/auth/RequireAuth';
import { AppLayout } from '../shared/ui/AppLayout';
import { appRoutes } from './routes';

export const router = createBrowserRouter(
  [
    {
      path: appRoutes.login,
      element: <LoginPage />
    },
    {
      element: <RequireAuth />,
      children: [
        {
          element: <AppLayout />,
          children: [
            {
              path: appRoutes.root,
              element: <Navigate to={appRoutes.dashboard} replace />
            },
            {
              path: appRoutes.dashboard,
              element: <DashboardPage />
            },
            {
              path: appRoutes.demoFeedback,
              element: <DemoFeedbackPage />
            },
            {
              path: appRoutes.report,
              element: <ReportPage />
            },
            {
              path: appRoutes.campaign,
              element: <CampaignPage />
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

