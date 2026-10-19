import { createTheme } from '@mui/material/styles';
import type { ThemeOptions } from '@mui/material/styles';
import { lightPanelSx, lightUiTokens as ui } from './lightUiTokens';

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

// Only AppLayout uses this theme. Keep the original theme above for the login screen.
const workspaceLightOptions: ThemeOptions = {
  palette: {
    background: { default: ui.background, paper: ui.surface.popup },
    primary: { main: ui.action.primary, dark: ui.action.link, contrastText: '#FFFFFF' },
    divider: ui.surface.divider,
    text: { primary: ui.text.primary, secondary: ui.text.secondary, disabled: ui.text.disabled },
    action: { hover: ui.surface.soft, selected: ui.action.selected, disabled: ui.text.disabled }
  },
  typography: {
    fontFamily: ui.fontFamily,
    h4: { fontSize: 28, fontWeight: 700, lineHeight: 1.2, letterSpacing: '-.02em', '@media (max-width:599.95px)': { fontSize: 22 } },
    h6: { fontSize: 18, fontWeight: 700, lineHeight: 1.3 },
    body1: { fontSize: 15, lineHeight: 1.5 },
    body2: { fontSize: 14, lineHeight: 1.5 },
    caption: { fontSize: 12, lineHeight: 1.4 },
    button: { fontSize: 14, fontWeight: 600, textTransform: 'none', lineHeight: 1.3 }
  },
  components: {
    MuiButtonBase: { styleOverrides: { root: { '&.Mui-focusVisible': ui.focus } } },
    MuiCard: { styleOverrides: { root: {
      ...lightPanelSx, border: `1px solid ${ui.surface.border}`, borderRadius: ui.radius.card,
      boxShadow: ui.surface.shadow, backgroundImage: 'none'
    } } },
    MuiCardContent: { styleOverrides: { root: {
      padding: 20, '&:last-child': { paddingBottom: 20 },
      '@media (max-width:599.95px)': { padding: 16, '&:last-child': { paddingBottom: 16 } }
    } } },
    MuiButton: { defaultProps: { disableElevation: true }, styleOverrides: {
      root: { minHeight: 36, padding: '7px 14px', borderRadius: ui.radius.card, boxShadow: 'none', '&:hover': { boxShadow: 'none' }, '&.Mui-disabled': { color: ui.text.disabled } },
      sizeSmall: { minHeight: 32, padding: '5px 12px', fontSize: 13 },
      outlined: { color: ui.text.primary, borderColor: ui.surface.controlBorder, backgroundColor: ui.surface.card,
        '&:hover': { borderColor: ui.surface.controlHover, backgroundColor: ui.surface.soft },
        '&.Mui-disabled': { borderColor: ui.surface.controlBorder } },
      outlinedError: { color: ui.status.error.color },
      text: ({ ownerState }) => ({ color: ownerState.color === 'error' ? ui.status.error.color : ui.action.link,
        '&:hover': { backgroundColor: ui.surface.soft } })
    } },
    MuiOutlinedInput: { styleOverrides: {
      root: { fontFamily: ui.fontFamily, fontSize: 14, borderRadius: ui.radius.control, backgroundColor: ui.surface.card,
        '&:not(.MuiInputBase-multiline)': { minHeight: 40 },
        '& .MuiOutlinedInput-notchedOutline': { borderColor: ui.surface.controlBorder },
        '&:hover:not(.Mui-disabled):not(.Mui-error) .MuiOutlinedInput-notchedOutline': { borderColor: ui.surface.controlHover },
        '&.Mui-focused': { outline: `2px solid ${ui.action.primary}`, outlineOffset: 2 },
        '&.Mui-focused .MuiOutlinedInput-notchedOutline': { borderColor: ui.action.primary },
        '&.Mui-error .MuiOutlinedInput-notchedOutline': { borderColor: ui.status.error.color },
        '&.Mui-disabled': { backgroundColor: ui.surface.soft }
      },
      input: { '&::placeholder': { color: ui.text.disabled, opacity: 1 } },
      inputSizeSmall: { padding: '8px 12px' },
      inputMultiline: { padding: 0 }
    } },
    MuiInputLabel: { styleOverrides: { root: { fontSize: 14 } } },
    MuiFormHelperText: { styleOverrides: { root: { fontSize: 12, lineHeight: 1.4 } } },
    MuiChip: { styleOverrides: {
      root: ({ ownerState }) => ({
        ...(ownerState.color === 'primary' ? ui.status.info : ownerState.color === 'secondary' ? ui.status.jam :
          ui.status[ownerState.color as keyof typeof ui.status] ?? ui.status.neutral),
        height: 24, maxWidth: '100%', borderRadius: ui.radius.badge, borderColor: 'transparent',
        fontSize: 12, fontWeight: 600,
        '& .MuiChip-label': { paddingLeft: 8, paddingRight: 8 },
        '&.Mui-disabled': { color: ui.text.disabled }
      }),
      sizeSmall: { height: 22 }
    } },
    MuiAlert: { styleOverrides: { root: ({ ownerState }) => ({
      ...ui.status[ownerState.severity ?? 'info'],
      borderRadius: ui.radius.control, fontSize: 14, '& .MuiAlert-icon': { color: 'inherit' },
      ...(ownerState.variant === 'outlined' ? { borderColor: 'currentColor' } : {})
    }) } },
    MuiTableHead: { styleOverrides: { root: { backgroundColor: 'transparent' } } },
    MuiTableCell: { styleOverrides: {
      root: { borderBottom: `1px solid ${ui.surface.divider}`, padding: '12px 16px', fontSize: 14 },
      head: { color: ui.text.secondary, fontSize: 12, fontWeight: 600, lineHeight: 1.3, textTransform: 'uppercase', letterSpacing: '.04em' },
      alignRight: { fontVariantNumeric: 'tabular-nums' },
      stickyHeader: { backgroundColor: ui.surface.popup }
    } },
    MuiTableRow: { styleOverrides: { root: {
      '&.MuiTableRow-hover:hover': { backgroundColor: ui.surface.soft },
      '&.Mui-selected': { backgroundColor: ui.action.selected },
      '&.Mui-selected:hover': { backgroundColor: ui.action.selected }
    } } },
    MuiTableSortLabel: { styleOverrides: { root: { '&.Mui-active': { color: ui.text.primary } } } },
    MuiPaginationItem: { styleOverrides: { root: {
      border: `1px solid ${ui.surface.controlBorder}`, borderRadius: ui.radius.pagination,
      color: ui.text.primary, backgroundColor: ui.surface.card, fontVariantNumeric: 'tabular-nums',
      '&:hover': { backgroundColor: ui.surface.soft },
      '&.Mui-selected': { color: '#FFFFFF', borderColor: ui.action.primary, backgroundColor: ui.action.primary },
      '&.Mui-selected:hover': { backgroundColor: ui.action.link },
      '&.Mui-disabled': { color: ui.text.disabled, opacity: 1 }
    } } },
    MuiToggleButton: { styleOverrides: { root: {
      fontFamily: ui.fontFamily, color: ui.text.body,
      '&.Mui-selected': { color: ui.text.primary, backgroundColor: ui.surface.card },
      '&.Mui-selected:hover': { backgroundColor: ui.surface.popup }
    } } },
    MuiMenu: { styleOverrides: { paper: { fontFamily: ui.fontFamily, backgroundColor: ui.surface.popup, borderRadius: ui.radius.control, boxShadow: ui.surface.popupShadow } } },
    MuiMenuItem: { styleOverrides: { root: { fontSize: 14, '&.Mui-selected': { backgroundColor: ui.action.selected } } } },
    MuiDialog: { styleOverrides: { paper: { fontFamily: ui.fontFamily, backgroundColor: ui.surface.popup, borderRadius: ui.radius.card } } },
    MuiTooltip: { styleOverrides: { tooltip: { fontFamily: ui.fontFamily } } },
    MuiLink: { styleOverrides: { root: { color: ui.action.link, '&:hover': { color: ui.action.linkHover } } } }
  }
};

// Recreate typography instead of merging a resolved theme: its variants already
// contain the login font family and would otherwise override the workspace font.
export const workspaceLightTheme = createTheme({
  ...workspaceLightOptions,
  palette: { ...theme.palette, ...workspaceLightOptions.palette },
  shape: theme.shape
});

