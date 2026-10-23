import DashboardIcon from '@mui/icons-material/Dashboard';
import { BrandLogo } from './BrandLogo';
import LogoutIcon from '@mui/icons-material/Logout';
import TelegramIcon from '@mui/icons-material/Telegram';
import StorefrontIcon from '@mui/icons-material/Storefront';
import TuneIcon from '@mui/icons-material/Tune';
import { Box, Chip, List, ListItemButton, ListItemIcon, ListItemText, Stack, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { NavLink, useLocation, useNavigate } from 'react-router-dom';
import { appRoutes } from '../../app/routes';
import { queryKeys } from '../api/queryKeys';
import { getCurrentUserFromApi } from '../auth/authApi';
import type { CurrentUser } from '../auth/authTypes';
import { clearAuth, setCurrentUser } from '../auth/tokenStorage';
import { getWbStores } from '../../pages/WbStoresPage/wbStoresApi';
import { lightUiTokens as ui } from '../../app/lightUiTokens';
import { DashboardDataStatus } from './DashboardDataStatus';
import { defaultPeriod } from '../../pages/DashboardPage/components/PeriodFilter';

const navItems = [
  { label: 'Сводка', to: appRoutes.dashboard, icon: <DashboardIcon fontSize="small" /> },
  { label: 'Результаты', to: appRoutes.results, icon: <TuneIcon fontSize="small" /> },
  { label: 'Нормы', to: appRoutes.norms, icon: <TuneIcon fontSize="small" /> },
  { label: 'Кабинет WB и Telegram', to: appRoutes.wbStores, icon: <StorefrontIcon fontSize="small" /> }
];

export function Sidebar({ mobile = false, onNavigate }: { mobile?: boolean; onNavigate?: () => void }) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const location = useLocation();
  const periodParams = new URLSearchParams(location.search);
  const startDate = periodParams.get('startDate') ?? defaultPeriod().startDate!;
  const endDate = periodParams.get('endDate') ?? defaultPeriod().endDate!;
  const stores = useQuery({ queryKey: ['wb-stores'], queryFn: getWbStores });
  const currentUserQuery = useQuery({
    queryKey: queryKeys.auth.me,
    queryFn: getCurrentUserFromApi
  });
  const currentUser = currentUserQuery.data;
  const demoState = getActiveDemoState(currentUser);

  useEffect(() => {
    if (currentUser) {
      setCurrentUser(currentUser);
    }
  }, [currentUser]);

  const logout = () => {
    clearAuth();
    queryClient.clear();
    onNavigate?.();
    navigate(appRoutes.site, { replace: true });
  };

  return (
    <Box
      component="aside"
      sx={{
        display: mobile ? 'flex' : { xs: 'none', md: 'flex' },
        flexDirection: 'column',
        width: mobile ? 280 : 240,
        height: '100dvh',
        flexShrink: 0,
        overflow: 'hidden',
        px: 2,
        py: 3,
        bgcolor: ui.surface.fallback,
        '@supports ((backdrop-filter: blur(1px)) or (-webkit-backdrop-filter: blur(1px)))': {
          bgcolor: ui.surface.sidebar, backdropFilter: ui.surface.sidebarBlur, WebkitBackdropFilter: ui.surface.sidebarBlur
        },
        fontFamily: ui.fontFamily,
        color: 'text.primary',
        borderRight: `1px solid ${ui.surface.border}`
      }}
    >
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 3, flexShrink: 0 }}>
        <BrandLogo height={32} />
      </Box>

      <Box sx={{ mb: 2.5, flexShrink: 0 }}>
        <Typography variant="caption" color="text.secondary">Кабинет WB</Typography>
        <Typography fontWeight={700} noWrap title={stores.data?.[0]?.name || 'Кабинет WB'}>
          {stores.data?.[0]?.name || 'Кабинет WB'}
        </Typography>
        {stores.data && stores.data.length > 1 ? <Typography variant="caption" color="text.secondary">Подключено: {stores.data.length}</Typography> : null}
      </Box>

      {demoState ? (
        <Stack spacing={0.5} sx={{ mb: 2, flexShrink: 0 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            <Chip label="Demo" size="small" sx={ui.status.info} />
            <Typography color="text.secondary" variant="body2">
              {demoState.timeLeftText}
            </Typography>
          </Box>
          {demoState.shouldWarn ? <Typography variant="caption" color="warning.main">После окончания демо для продления доступа потребуется отзыв.</Typography> : null}
        </Stack>
      ) : null}

      <List sx={{ flex: 1, minHeight: 0, overflowY: 'auto', p: 0 }}>
        {currentUser?.canManagePilotApplications && <ListItemButton component={NavLink} to={appRoutes.pilotApplications} onClick={onNavigate}>
          <ListItemText primary="Заявки в пилот" />
        </ListItemButton>}
        {navItems.map((item) => (
          <ListItemButton
            key={item.label}
            component={NavLink}
            className={item.to === appRoutes.dashboard && location.pathname.startsWith('/campaign/') ? 'active' : ''}
            onClick={onNavigate}
            to={item.to === appRoutes.dashboard && location.pathname.startsWith('/campaign/') ? `${item.to}${location.search}` : item.to}
            sx={{
              mb: 0.5,
              minHeight: 42,
              px: 1.5,
              borderRadius: ui.radius.control,
              color: ui.text.body,
              '&.active': {
                bgcolor: ui.action.selected,
                color: 'primary.dark',
                '& .MuiListItemIcon-root': { color: 'primary.main' }
              },
              '&:hover': { bgcolor: ui.surface.soft },
            }}
          >
            <ListItemIcon sx={{ color: 'inherit', minWidth: 32 }}>{item.icon}</ListItemIcon>
            <ListItemText primary={item.label} primaryTypographyProps={{ fontSize: 15, fontWeight: 500, lineHeight: 1.3 }} />
          </ListItemButton>
        ))}
      </List>

      {!mobile && location.pathname === appRoutes.dashboard ? <DashboardDataStatus startDate={startDate} endDate={endDate} sidebar /> : null}
      <Box sx={{ borderTop: '1px solid', borderColor: 'divider', pt: 1, flexShrink: 0 }}>
      <ListItemButton
        component="a"
        href="https://t.me/ecomads_mvp01"
        rel="noopener noreferrer"
        target="_blank"
        sx={{ minHeight: 42, px: 1.5, borderRadius: ui.radius.control, color: 'text.secondary' }}
      >
        <ListItemIcon sx={{ color: 'inherit', minWidth: 32 }}>
          <TelegramIcon fontSize="small" />
        </ListItemIcon>
        <ListItemText primary="Группа в Telegram" primaryTypographyProps={{ fontSize: 13, lineHeight: 1.2 }} />
      </ListItemButton>

      <ListItemButton onClick={logout} sx={{ minHeight: 42, px: 1.5, borderRadius: ui.radius.control, color: 'text.secondary' }}>
        <ListItemIcon sx={{ color: 'inherit', minWidth: 32 }}>
          <LogoutIcon fontSize="small" />
        </ListItemIcon>
        <ListItemText primary="Выход" primaryTypographyProps={{ fontSize: 13, lineHeight: 1.2 }} />
      </ListItemButton>
      </Box>
    </Box>
  );
}

type ActiveDemoState = {
  timeLeftText: string;
  shouldWarn: boolean;
};

function getActiveDemoState(user?: CurrentUser): ActiveDemoState | null {
  if (!user?.isDemoUser || user.accessType !== 1 || user.demoStatus !== 1 || !user.demoExpiresAtUtc) {
    return null;
  }

  const expiresAt = Date.parse(user.demoExpiresAtUtc);
  if (Number.isNaN(expiresAt)) {
    return null;
  }

  const diffMs = expiresAt - Date.now();
  if (diffMs <= 0) {
    return null;
  }

  const dayMs = 24 * 60 * 60 * 1000;
  if (diffMs < dayMs) {
    return {
      timeLeftText: 'Осталось меньше 24 часов',
      shouldWarn: true
    };
  }

  const daysLeft = Math.ceil(diffMs / dayMs);
  return {
    timeLeftText: daysLeft === 1 ? 'Остался 1 день' : `Осталось ${daysLeft} ${getDayWord(daysLeft)}`,
    shouldWarn: false
  };
}

function getDayWord(days: number): string {
  const lastTwoDigits = days % 100;
  if (lastTwoDigits >= 11 && lastTwoDigits <= 14) {
    return 'дней';
  }

  const lastDigit = days % 10;
  if (lastDigit >= 2 && lastDigit <= 4) {
    return 'дня';
  }

  return 'дней';
}
