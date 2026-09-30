import DashboardIcon from '@mui/icons-material/Dashboard';
import LogoutIcon from '@mui/icons-material/Logout';
import TelegramIcon from '@mui/icons-material/Telegram';
import StorefrontIcon from '@mui/icons-material/Storefront';
import TuneIcon from '@mui/icons-material/Tune';
import { Box, Chip, List, ListItemButton, ListItemIcon, ListItemText, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useEffect } from 'react';
import { NavLink, useLocation, useNavigate } from 'react-router-dom';
import { appRoutes } from '../../app/routes';
import { queryKeys } from '../api/queryKeys';
import { getCurrentUserFromApi } from '../auth/authApi';
import type { CurrentUser } from '../auth/authTypes';
import { clearAuth, setCurrentUser } from '../auth/tokenStorage';
import { getWbStores } from '../../pages/WbStoresPage/wbStoresApi';

const navItems = [
  { label: 'Сводка', to: appRoutes.dashboard, icon: <DashboardIcon fontSize="small" /> },
  { label: 'Нормы', to: appRoutes.norms, icon: <TuneIcon fontSize="small" /> },
  { label: 'Кабинет WB и Telegram', to: appRoutes.wbStores, icon: <StorefrontIcon fontSize="small" /> }
];

export function Sidebar({ mobile = false, onNavigate }: { mobile?: boolean; onNavigate?: () => void }) {
  const navigate = useNavigate();
  const location = useLocation();
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
    onNavigate?.();
    navigate(appRoutes.login, { replace: true });
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
        p: 2,
        bgcolor: 'rgba(255,255,255,0.72)',
        backdropFilter: 'blur(22px)',
        color: 'text.primary',
        borderRight: '1px solid rgba(255,255,255,0.85)'
      }}
    >
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 3, flexShrink: 0 }}>
        <Box sx={{ width: 30, height: 30, borderRadius: '8px', bgcolor: 'primary.main', color: '#fff',
          display: 'grid', placeItems: 'center', fontWeight: 800, flexShrink: 0 }}>E</Box>
        <Typography variant="h6" fontWeight={800}>
          EcomAds
        </Typography>
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
            <Chip label="Demo" size="small" sx={{ bgcolor: '#E8F2FF', color: '#0068D9', fontWeight: 800 }} />
            <Typography color="text.secondary" variant="body2">
              {demoState.timeLeftText}
            </Typography>
          </Box>
          {demoState.shouldWarn ? <Typography variant="caption" color="warning.main">Подробнее о доступе — в профиле</Typography> : null}
        </Stack>
      ) : null}

      <List sx={{ flex: 1, minHeight: 0, overflowY: 'auto', p: 0 }}>
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
              borderRadius: '10px',
              color: 'text.secondary',
              '&.active': {
                bgcolor: 'rgba(0,122,255,0.1)',
                color: 'primary.dark',
                '& .MuiListItemIcon-root': { color: 'primary.main' }
              },
              '&:hover': { bgcolor: 'rgba(0,122,255,0.07)' },
            }}
          >
            <ListItemIcon sx={{ color: 'inherit', minWidth: 32 }}>{item.icon}</ListItemIcon>
            <ListItemText primary={item.label} primaryTypographyProps={{ fontSize: 13, lineHeight: 1.2 }} />
          </ListItemButton>
        ))}
      </List>

      <Box sx={{ borderTop: '1px solid', borderColor: 'divider', pt: 1, flexShrink: 0 }}>
      <ListItemButton
        component="a"
        href="https://t.me/ecomads_mvp01"
        rel="noopener noreferrer"
        target="_blank"
        sx={{ minHeight: 42, px: 1.5, borderRadius: '10px', color: 'text.secondary' }}
      >
        <ListItemIcon sx={{ color: 'inherit', minWidth: 32 }}>
          <TelegramIcon fontSize="small" />
        </ListItemIcon>
        <ListItemText primary="Группа в Telegram" />
      </ListItemButton>

      <ListItemButton onClick={logout} sx={{ minHeight: 42, px: 1.5, borderRadius: '10px', color: 'text.secondary' }}>
        <ListItemIcon sx={{ color: 'inherit', minWidth: 32 }}>
          <LogoutIcon fontSize="small" />
        </ListItemIcon>
        <ListItemText primary="Выход" />
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
