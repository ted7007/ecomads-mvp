import { createTheme } from '@mui/material/styles';

export const theme = createTheme({
  palette: {
    mode: 'light',
    primary: {
      main: '#FF6B4A',
      dark: '#E85A3A',
      contrastText: '#FFFFFF'
    },
    success: {
      main: '#10B981'
    },
    warning: {
      main: '#F59E0B'
    },
    error: {
      main: '#EF4444'
    },
    background: {
      default: '#F8FAFC',
      paper: '#FFFFFF'
    },
    text: {
      primary: '#0F172A',
      secondary: '#64748B'
    },
    divider: '#E2E8F0'
  },
  typography: {
    fontFamily: '"Inter", "Roboto", "Arial", sans-serif',
    button: {
      textTransform: 'none',
      fontWeight: 600
    }
  },
  shape: {
    borderRadius: 16
  },
  components: {
    MuiCard: {
      styleOverrides: {
        root: {
          backgroundColor: '#F8FAFC',
          boxShadow: '0 20px 25px -12px rgba(0,0,0,0.5)'
        }
      }
    },
    MuiButton: {
      defaultProps: {
        disableElevation: true
      }
    }
  }
});

