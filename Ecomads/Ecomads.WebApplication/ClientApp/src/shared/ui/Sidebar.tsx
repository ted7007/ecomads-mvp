import DashboardIcon from '@mui/icons-material/Dashboard';
import LogoutIcon from '@mui/icons-material/Logout';
import TelegramIcon from '@mui/icons-material/Telegram';
import StorefrontIcon from '@mui/icons-material/Storefront';
import TuneIcon from '@mui/icons-material/Tune';
import { Alert, Box, Chip, List, ListItemButton, ListItemIcon, ListItemText, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useEffect } from 'react';
import { NavLink, useNavigate } from 'react-router-dom';
import { appRoutes } from '../../app/routes';
import { queryKeys } from '../api/queryKeys';
import { getCurrentUserFromApi } from '../auth/authApi';
import type { CurrentUser } from '../auth/authTypes';
import { clearAuth, setCurrentUser } from '../auth/tokenStorage';

const navItems = [
  { label: 'Сводка', to: appRoutes.dashboard, icon: <DashboardIcon fontSize="small" /> },
  { label: 'Кабинеты WB', to: appRoutes.wbStores, icon: <StorefrontIcon fontSize="small" /> },
  { label: 'Нормы', to: appRoutes.norms, icon: <TuneIcon fontSize="small" /> }
];

export function Sidebar({ mobile = false, onNavigate }: { mobile?: boolean; onNavigate?: () => void }) {
  const navigate = useNavigate();
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
        width: 260,
        height: mobile ? '100%' : '100vh',
        flexShrink: 0,
        overflow: 'hidden',
        p: 3,
        bgcolor: 'rgba(255,255,255,0.72)',
        backdropFilter: 'blur(22px)',
        color: 'text.primary',
        borderRight: '1px solid rgba(255,255,255,0.85)'
      }}
    >
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 4 }}>
        <Typography variant="h6" fontWeight={800}>
          EcomAds
        </Typography>
        <Chip label="MVP" size="small" sx={{ bgcolor: '#E8F2FF', color: '#0068D9', fontWeight: 700 }} />
      </Box>

      {demoState ? (
        <Stack spacing={1.25} sx={{ mb: 3 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            <Chip label="Demo" size="small" sx={{ bgcolor: '#E8F2FF', color: '#0068D9', fontWeight: 800 }} />
            <Typography color="text.secondary" variant="body2">
              {demoState.timeLeftText}
            </Typography>
          </Box>
          {demoState.shouldWarn ? (
            <Alert severity="warning" sx={{ py: 0.5, '& .MuiAlert-message': { fontSize: 13 } }}>
              Демо-доступ скоро закончится. После окончания нужно будет оставить обратную связь, чтобы продолжить пользоваться MVP.
            </Alert>
          ) : null}
        </Stack>
      ) : null}

      <List sx={{ flex: 1, minHeight: 0, overflowY: 'auto' }}>
        {navItems.map((item) => (
          <ListItemButton
            key={item.label}
            component={NavLink}
            onClick={onNavigate}
            to={item.to}
            sx={{
              mb: 1,
              borderRadius: 3,
              color: 'text.secondary',
              '&.active': {
                bgcolor: 'rgba(0,122,255,0.1)',
                color: 'primary.dark',
                '& .MuiListItemIcon-root': { color: 'primary.main' }
              },
              '&:hover': { bgcolor: 'rgba(0,122,255,0.07)' },
            }}
          >
            <ListItemIcon sx={{ color: 'inherit', minWidth: 36 }}>{item.icon}</ListItemIcon>
            <ListItemText primary={item.label} />
          </ListItemButton>
        ))}
      </List>

      <ListItemButton
        component="a"
        href="https://t.me/ecomads_mvp01"
        rel="noopener noreferrer"
        target="_blank"
        sx={{ flexShrink: 0, mb: 1, borderRadius: 3, color: 'text.secondary' }}
      >
        <ListItemIcon sx={{ color: 'inherit', minWidth: 36 }}>
          <TelegramIcon fontSize="small" />
        </ListItemIcon>
        <ListItemText primary="Группа в Telegram" />
      </ListItemButton>

      <ListItemButton onClick={logout} sx={{ flexShrink: 0, mt: 2, borderRadius: 3, color: 'text.secondary' }}>
        <ListItemIcon sx={{ color: 'inherit', minWidth: 36 }}>
          <LogoutIcon fontSize="small" />
        </ListItemIcon>
        <ListItemText primary="Выход" />
      </ListItemButton>
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
