import { createTheme } from '@mui/material/styles';

export const theme = createTheme({
  palette: {
    mode: 'light',
    primary: {
      main: '#007AFF',
      dark: '#0068D9',
      contrastText: '#FFFFFF'
    },
    success: {
      main: '#1A7F45'
    },
    warning: {
      main: '#A84B00'
    },
    error: {
      main: '#C4261B'
    },
    background: {
      default: '#F2F3F8',
      paper: 'rgba(255,255,255,0.72)'
    },
    text: {
      primary: '#1D1D1F',
      secondary: '#5C5C66'
    },
    divider: 'rgba(80,90,120,0.14)'
  },
  typography: {
    fontFamily: '"Golos Text", "Inter", "Roboto", "Arial", sans-serif',
    button: {
      textTransform: 'none',
      fontWeight: 600
    }
  },
  shape: {
    borderRadius: 8
  },
  components: {
    MuiCard: {
      styleOverrides: {
        root: {
          backgroundColor: 'rgba(255,255,255,0.72)',
          border: '1px solid rgba(255,255,255,0.85)',
          backdropFilter: 'blur(18px)',
          boxShadow: '0 6px 22px rgba(40,50,90,0.07)',
          borderRadius: 18
        }
      }
    },
    MuiTableHead: {
      styleOverrides: {
        root: { backgroundColor: 'rgba(243,246,253,0.8)' }
      }
    },
    MuiTableRow: {
      styleOverrides: {
        root: { '&:last-child td': { borderBottom: 0 } }
      }
    },
    MuiButton: {
      defaultProps: {
        disableElevation: true
      },
      styleOverrides: { root: { borderRadius: 18 } }
    }
  }
});

