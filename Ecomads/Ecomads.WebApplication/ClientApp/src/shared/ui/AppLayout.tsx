import MenuIcon from '@mui/icons-material/Menu';
import { Box, Drawer, IconButton, Typography } from '@mui/material';
import { useState } from 'react';
import { Outlet } from 'react-router-dom';
import { Sidebar } from './Sidebar';

export function AppLayout() {
  const [menuOpen, setMenuOpen] = useState(false);
  return (
    <Box sx={{ display: 'flex', height: '100dvh', overflow: 'hidden', background: 'radial-gradient(circle at 7% 4%, #D8E6FF 0, transparent 29%), radial-gradient(circle at 92% 4%, #FFE9DA 0, transparent 28%), radial-gradient(circle at 55% 94%, #EDE8FF 0, transparent 36%), #F2F3F8' }}>
      <Sidebar />
      <Box sx={{ display: { xs: 'flex', md: 'none' }, alignItems: 'center', position: 'fixed', inset: '0 0 auto 0', zIndex: 1100, height: 60, px: 2, gap: 1.5, bgcolor: 'rgba(255,255,255,0.8)', backdropFilter: 'blur(18px)', borderBottom: '1px solid rgba(255,255,255,0.85)' }}>
        <IconButton aria-label="Открыть меню" onClick={() => setMenuOpen(true)}><MenuIcon /></IconButton>
        <Typography fontWeight={800}>EcomAds</Typography>
      </Box>
      <Drawer open={menuOpen} onClose={() => setMenuOpen(false)} sx={{ display: { xs: 'block', md: 'none' } }} slotProps={{ paper: { sx: { width: 280, bgcolor: 'transparent', boxShadow: 'none' } } }}>
        <Sidebar mobile onNavigate={() => setMenuOpen(false)} />
      </Drawer>
      <Box
        component="main"
        sx={{
          flex: 1,
          minWidth: 0,
          height: '100dvh',
          overflowY: 'auto',
          overscrollBehavior: 'contain',
          px: { xs: 2, md: 3, lg: 5 },
          py: { xs: 9, md: 3, lg: 4 }
        }}
      >
        <Outlet />
      </Box>
    </Box>
  );
}
