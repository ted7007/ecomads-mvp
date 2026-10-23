import MenuIcon from '@mui/icons-material/Menu';
import { Box, Drawer, IconButton, Typography } from '@mui/material';
import { ThemeProvider } from '@mui/material/styles';
import { useState } from 'react';
import { Outlet } from 'react-router-dom';
import { Sidebar } from './Sidebar';
import { workspaceLightTheme } from '../../app/theme';
import { lightUiTokens as ui } from '../../app/lightUiTokens';
import '../assets/fonts/golos-text.css';
import { BrandLogo } from './BrandLogo';

export function AppLayout() {
  const [menuOpen, setMenuOpen] = useState(false);
  return (
    <ThemeProvider theme={workspaceLightTheme}>
    <Box sx={{ display: 'flex', height: '100dvh', overflow: 'hidden', background: ui.gradient,
      fontFamily: ui.fontFamily, fontSize: 15, lineHeight: 1.5, color: ui.text.primary }}>
      <Sidebar />
      <Box sx={{ display: { xs: 'flex', md: 'none' }, alignItems: 'center', position: 'fixed', inset: '0 0 auto 0', zIndex: 1100, height: 60, px: 2, gap: 1.5,
        bgcolor: ui.surface.fallback, borderBottom: `1px solid ${ui.surface.border}`,
        '@supports ((backdrop-filter: blur(1px)) or (-webkit-backdrop-filter: blur(1px)))': {
          bgcolor: ui.surface.popup, backdropFilter: ui.surface.blur, WebkitBackdropFilter: ui.surface.blur
        } }}>
        <IconButton aria-label="Открыть меню" onClick={() => setMenuOpen(true)}><MenuIcon /></IconButton>
        <BrandLogo height={28} />
      </Box>
      <Drawer open={menuOpen} onClose={() => setMenuOpen(false)} sx={{ display: { xs: 'block', md: 'none' } }} slotProps={{ paper: { sx: { width: 280, bgcolor: ui.background, boxShadow: 'none' } } }}>
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
          pt: { xs: 9, md: '28px' },
          pb: { xs: 3, md: '28px' }
        }}
      >
        <Outlet />
      </Box>
    </Box>
    </ThemeProvider>
  );
}
