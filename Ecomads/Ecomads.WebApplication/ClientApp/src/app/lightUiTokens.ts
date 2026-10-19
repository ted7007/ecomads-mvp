/** Light workspace styling from the approved v10 mockup. The login theme is separate. */
export const lightUiTokens = {
  fontFamily: '"EcomAds Golos Text", "Inter", "Arial", sans-serif',
  background: '#F2F3F8',
  gradient: 'radial-gradient(1100px 700px at 6% 0%, #C9DCFF 0, rgba(201,220,255,0) 62%), radial-gradient(900px 640px at 100% 10%, #FFE1CF 0, rgba(255,225,207,0) 58%), radial-gradient(1000px 800px at 55% 100%, #E3D9FF 0, rgba(227,217,255,0) 62%), #F2F3F8',
  surface: {
    sidebar: 'rgba(255,255,255,.52)',
    card: 'rgba(255,255,255,.66)',
    popup: 'rgba(255,255,255,.94)',
    fallback: '#F9FAFD',
    border: 'rgba(255,255,255,.85)',
    divider: 'rgba(30,30,60,.08)',
    controlBorder: 'rgba(30,30,60,.14)',
    controlHover: 'rgba(30,30,60,.28)',
    soft: 'rgba(118,118,140,.10)',
    segment: 'rgba(118,118,140,.14)',
    shadow: '0 6px 22px rgba(40,50,90,.07)',
    popupShadow: '0 10px 30px rgba(40,50,90,.14)',
    blur: 'blur(22px) saturate(170%)',
    sidebarBlur: 'blur(28px) saturate(180%)'
  },
  text: { primary: '#1D1D1F', body: '#3A3A40', secondary: '#5C5C66', disabled: '#9A9AA2' },
  action: { primary: '#007AFF', link: '#0068D9', linkHover: '#0052AD', selected: 'rgba(0,122,255,.13)' },
  status: {
    success: { color: '#1A7F45', backgroundColor: 'rgba(52,199,89,.16)' },
    warning: { color: '#A84B00', backgroundColor: 'rgba(255,149,0,.15)' },
    error: { color: '#C4261B', backgroundColor: 'rgba(255,59,48,.12)' },
    neutral: { color: '#48484E', backgroundColor: 'rgba(118,118,128,.14)' },
    info: { color: '#0068D9', backgroundColor: 'rgba(0,122,255,.13)' },
    jam: { color: '#6A3BC2', backgroundColor: 'rgba(175,82,222,.15)' }
  },
  jamCell: { color: '#6A3BC2', backgroundColor: 'rgba(175,82,222,.07)' },
  radius: { card: '18px', control: '8px', badge: '5px', pagination: '10px' },
  focus: { outline: '2px solid #007AFF', outlineOffset: 2 }
} as const;

/** Opaque fallback also covers browsers without either backdrop-filter implementation. */
export const lightPanelSx = {
  backgroundColor: lightUiTokens.surface.fallback,
  '@supports ((backdrop-filter: blur(1px)) or (-webkit-backdrop-filter: blur(1px)))': {
    backgroundColor: lightUiTokens.surface.card,
    backdropFilter: lightUiTokens.surface.blur,
    WebkitBackdropFilter: lightUiTokens.surface.blur
  }
} as const;
