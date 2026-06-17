import BarChartIcon from '@mui/icons-material/BarChart';
import DashboardIcon from '@mui/icons-material/Dashboard';
import LayersIcon from '@mui/icons-material/Layers';
import LogoutIcon from '@mui/icons-material/Logout';
import TelegramIcon from '@mui/icons-material/Telegram';
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
  { label: 'Обзор рекламы', to: appRoutes.dashboard, icon: <DashboardIcon fontSize="small" /> },
  { label: 'Кампания', to: appRoutes.campaignPath('placeholder'), icon: <LayersIcon fontSize="small" />, disabled: true },
  { label: 'Прогноз эффекта', to: appRoutes.report, icon: <BarChartIcon fontSize="small" /> }
];

export function Sidebar() {
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
    navigate(appRoutes.login, { replace: true });
  };

  return (
    <Box
      component="aside"
      sx={{
        display: { xs: 'none', md: 'flex' },
        flexDirection: 'column',
        width: 260,
        height: '100vh',
        flexShrink: 0,
        overflow: 'hidden',
        p: 3,
        bgcolor: '#1E293B',
        color: '#E5E7EB',
        borderRight: '1px solid rgba(255,255,255,0.08)'
      }}
    >
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 4 }}>
        <Typography variant="h6" fontWeight={800}>
          EcomAds
        </Typography>
        <Chip label="MVP" size="small" sx={{ bgcolor: '#F97316', color: '#FFFFFF', fontWeight: 700 }} />
      </Box>

      {demoState ? (
        <Stack spacing={1.25} sx={{ mb: 3 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            <Chip label="Demo" size="small" sx={{ bgcolor: '#38BDF8', color: '#082F49', fontWeight: 800 }} />
            <Typography color="#E0F2FE" variant="body2">
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
            disabled={item.disabled}
            to={item.to}
            sx={{
              mb: 1,
              borderRadius: 3,
              color: '#CBD5E1',
              '&.active': {
                bgcolor: 'primary.main',
                color: '#FFFFFF',
                '& .MuiListItemIcon-root': { color: '#FFFFFF' }
              },
              '&.Mui-disabled': {
                color: '#94A3B8',
                opacity: 0.6
              }
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
        sx={{ flexShrink: 0, mb: 1, borderRadius: 3, color: '#CBD5E1' }}
      >
        <ListItemIcon sx={{ color: 'inherit', minWidth: 36 }}>
          <TelegramIcon fontSize="small" />
        </ListItemIcon>
        <ListItemText primary="Группа в Telegram" />
      </ListItemButton>

      <ListItemButton onClick={logout} sx={{ flexShrink: 0, mt: 2, borderRadius: 3, color: '#CBD5E1' }}>
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
