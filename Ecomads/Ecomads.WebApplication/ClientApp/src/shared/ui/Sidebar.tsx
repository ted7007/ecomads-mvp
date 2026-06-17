import BarChartIcon from '@mui/icons-material/BarChart';
import DashboardIcon from '@mui/icons-material/Dashboard';
import LayersIcon from '@mui/icons-material/Layers';
import LogoutIcon from '@mui/icons-material/Logout';
import { Box, Chip, List, ListItemButton, ListItemIcon, ListItemText, Typography } from '@mui/material';
import { NavLink, useNavigate } from 'react-router-dom';
import { appRoutes } from '../../app/routes';
import { clearAuth } from '../auth/tokenStorage';

const navItems = [
  { label: 'Дашборд', to: appRoutes.dashboard, icon: <DashboardIcon fontSize="small" /> },
  { label: 'Кампания', to: appRoutes.campaignPath('placeholder'), icon: <LayersIcon fontSize="small" />, disabled: true },
  { label: 'Отчёт эффект.', to: appRoutes.report, icon: <BarChartIcon fontSize="small" /> }
];

export function Sidebar() {
  const navigate = useNavigate();

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
        minHeight: '100vh',
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

      <List sx={{ flex: 1 }}>
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

      <ListItemButton onClick={logout} sx={{ borderRadius: 3, color: '#CBD5E1' }}>
        <ListItemIcon sx={{ color: 'inherit', minWidth: 36 }}>
          <LogoutIcon fontSize="small" />
        </ListItemIcon>
        <ListItemText primary="Выход" />
      </ListItemButton>
    </Box>
  );
}
