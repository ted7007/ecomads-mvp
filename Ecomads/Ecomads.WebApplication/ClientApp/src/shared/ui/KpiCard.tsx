import { Box, ButtonBase } from '@mui/material';
import type { ReactNode } from 'react';
import type { KpiTone } from '../lib/kpiComparison';

const toneColor: Record<KpiTone, string> = { good: 'success.main', bad: 'error.main', neutral: '#3A3A40' };

export type KpiCardProps = {
  label: string;
  color: string;
  value: string;
  valueColor?: string;
  delta?: string;
  deltaTone?: KpiTone;
  /** «было …»: на телефоне скрывается, как в макете. */
  previous?: string;
  note?: string;
  selected?: boolean;
  /** Без обработчика карточка показывается как недоступная и не переключает линию. */
  onToggle?: () => void;
  unavailableReason?: string;
  hint?: string;
};

export function KpiGrid({ children }: { children: ReactNode }) {
  return <Box sx={{
    display: 'grid', width: '100%', gap: { xs: '10px', sm: '14px' },
    gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', md: 'repeat(3, minmax(0, 1fr))', lg: 'repeat(5, minmax(0, 1fr))' },
    '& > :last-child:nth-of-type(odd)': { gridColumn: { xs: 'span 2', md: 'auto' } }
  }}>{children}</Box>;
}

export function KpiCard({ label, color, value, valueColor, delta, deltaTone = 'neutral', previous, note, selected = false,
  onToggle, unavailableReason, hint }: KpiCardProps) {
  const unavailable = !onToggle;
  const sx = {
    display: 'flex', flexDirection: 'column', alignItems: 'stretch', justifyContent: 'flex-start', gap: { xs: 0.5, sm: 0.75 },
    width: '100%', height: '100%', minWidth: 0, textAlign: 'left', fontFamily: 'inherit', color: 'text.primary',
    p: { xs: '12px 14px', sm: '14px 16px' }, borderRadius: '18px', border: '1px solid',
    borderColor: selected ? color : 'rgba(255,255,255,.85)', bgcolor: 'rgba(255,255,255,.66)',
    backdropFilter: 'blur(22px) saturate(170%)', boxShadow: selected ? `inset 0 3px 0 ${color}` : 'none',
    transition: 'border-color .15s, box-shadow .15s',
    ...(unavailable ? { cursor: 'default' } : {
      cursor: 'pointer',
      '&:hover': selected ? undefined : { borderColor: 'rgba(30,30,60,.28)' },
      '&.Mui-focusVisible': { outline: '2px solid #007AFF', outlineOffset: 2 }
    })
  } as const;
  const content = <>
    <Box component="span" sx={{ display: 'flex', alignItems: 'center', gap: { xs: 0.75, sm: 1 }, minWidth: 0,
      fontSize: { xs: 13, sm: 14 }, fontWeight: 500, lineHeight: 1.2, color: unavailable ? 'text.secondary' : '#3A3A40' }}>
      <Box component="span" aria-hidden sx={{ flex: 'none', width: { xs: 10, sm: 12 }, height: { xs: 10, sm: 12 }, borderRadius: '3px',
        border: `2px ${unavailable ? 'dashed' : 'solid'} ${color}`, bgcolor: selected ? color : 'transparent', opacity: unavailable ? 0.55 : 1 }} />
      <Box component="span" sx={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{label}</Box>
    </Box>
    <Box component="span" sx={{ fontSize: { xs: 19, sm: 24 }, fontWeight: 700, letterSpacing: '-0.02em', lineHeight: 1.2,
      fontVariantNumeric: 'tabular-nums', overflowWrap: 'anywhere', color: valueColor ?? (unavailable ? '#9A9AA2' : 'text.primary') }}>
      {value}
    </Box>
    <Box component="span" sx={{ fontSize: 13, lineHeight: 1.25, minHeight: 16, fontVariantNumeric: 'tabular-nums' }}>
      {delta ? <Box component="span" sx={{ fontWeight: 600, color: toneColor[deltaTone] }}>{delta}</Box> : null}
      {previous ? <Box component="span" sx={{ color: 'text.secondary', display: delta ? { xs: 'none', sm: 'inline' } : 'inline' }}>
        {delta ? ' ' : ''}было {previous}</Box> : null}
      {note ? <Box component="span" sx={{ color: 'text.secondary' }}>{delta || previous ? ' ' : ''}{note}</Box> : null}
      {!delta && !previous && !note ? '\u00a0' : null}
    </Box>
  </>;

  return unavailable ?
    <Box role="group" aria-label={`${label}: ${unavailableReason ?? 'нет данных'}`} title={unavailableReason} sx={sx}>{content}</Box> :
    <ButtonBase onClick={onToggle} aria-pressed={selected} title={hint} aria-label={`${label}: ${value}. ${selected ? 'Убрать линию с графика' : 'Показать на графике'}`}
      sx={sx}>{content}</ButtonBase>;
}
