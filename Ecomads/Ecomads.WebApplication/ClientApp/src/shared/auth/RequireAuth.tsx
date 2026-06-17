import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { appRoutes } from '../../app/routes';
import { getToken } from './tokenStorage';

export function RequireAuth() {
  const location = useLocation();
  const token = getToken();

  if (!token) {
    return <Navigate to={appRoutes.login} replace state={{ from: location }} />;
  }

  return <Outlet />;
}

