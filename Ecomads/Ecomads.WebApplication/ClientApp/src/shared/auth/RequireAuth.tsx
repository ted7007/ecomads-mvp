import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { appRoutes } from '../../app/routes';
import { clearAuth } from './tokenStorage';
import { useSession, SessionStatus } from './useSession';
import { withReturnTo } from './returnTo';

export function RequireAuth() {
  const location = useLocation();
  const session = useSession();

  if (!session.token || session.invalid) {
    if (session.invalid) clearAuth();
    return <Navigate to={withReturnTo(appRoutes.site, location.pathname + location.search + location.hash)} replace />;
  }
  if (session.isPending) return <SessionStatus retry={() => void session.refetch()} />;
  if (session.isError) return <SessionStatus error retry={() => void session.refetch()} />;

  return <Outlet />;
}

