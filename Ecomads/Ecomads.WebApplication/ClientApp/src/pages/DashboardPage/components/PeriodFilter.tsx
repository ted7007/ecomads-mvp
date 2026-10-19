import { Button, MenuItem, Select, Stack, TextField, ToggleButton, ToggleButtonGroup } from '@mui/material';
import { useState } from 'react';
import type { LoadedPeriod } from '../../../shared/api/apiTypes';
import { lightUiTokens as ui } from '../../../app/lightUiTokens';
import type { DashboardFilters } from '../dashboardApi';

type Props = {
  draftFilters: DashboardFilters;
  periods: LoadedPeriod[];
  onDraftChange: (filters: DashboardFilters) => void;
  onApply: (filters: DashboardFilters) => void;
  compact?: boolean;
};

export function PeriodFilter({ draftFilters, onDraftChange, onApply, compact = false }: Props) {
  const [customOpen, setCustomOpen] = useState(false);
  const today = moscowToday();
  const end = addDays(today, -1);
  const presets = [7, 14, 30, 365].map((days) => ({
    days, filters: { startDate: addDays(end, 1 - days), endDate: end }
  }));
  const selected = customOpen ? 'custom' : draftFilters.startDate === today && draftFilters.endDate === today ? 'today' : presets.find(({ filters }) => filters.startDate === draftFilters.startDate &&
    filters.endDate === draftFilters.endDate)?.days ?? 'custom';
  const invalid = !draftFilters.startDate || !draftFilters.endDate ||
    draftFilters.startDate > draftFilters.endDate || draftFilters.endDate > today ||
    (Date.parse(`${draftFilters.endDate}T00:00:00Z`) - Date.parse(`${draftFilters.startDate}T00:00:00Z`)) / 86400000 > 365;

  return <Stack spacing={1}>
    <Stack direction={compact ? 'row' : { xs: 'column', sm: 'row' }} gap={1} alignItems="center" justifyContent="space-between">
      <ToggleButtonGroup exclusive size="small" value={selected} aria-label="Период статистики"
        onChange={(_, value) => {
          if (value === 'today') {
            const next = { startDate: today, endDate: today };
            setCustomOpen(false); onDraftChange(next); onApply(next); return;
          }
          const days = Number(value);
          if (!Number.isFinite(days)) return;
          const next = presets.find((preset) => preset.days === days)?.filters;
          if (!next) return;
          setCustomOpen(false);
          onDraftChange(next);
          onApply(next);
        }} sx={{ bgcolor: ui.surface.segment, borderRadius: '999px', p: .4,
          maxWidth: '100%', overflowX: 'auto',
          '& .MuiToggleButton-root': { border: 0, borderRadius: '999px!important', px: { xs: 1, sm: 2.5 },
            fontSize: { xs: 12, sm: 13 }, whiteSpace: 'nowrap', textTransform: 'none' } }}>
        {!compact ? <ToggleButton value="today">Сегодня</ToggleButton> : null}
        {presets.filter(({ days }) => !compact || days !== 365).map(({ days }) => <ToggleButton key={days} value={days}>{days === 365 ? 'Год' : `${days} дней`}</ToggleButton>)}
        <ToggleButton value="custom" onClick={() => setCustomOpen(true)} sx={compact ? { display: { xs: 'none', sm: 'inline-flex' } } : undefined}>Свой период</ToggleButton>
      </ToggleButtonGroup>
      {compact ? <Select size="small" value={selected === 'today' || selected === 365 ? String(selected) : 'more'}
        inputProps={{ 'aria-label': 'Другие периоды' }} sx={{ minWidth: 105 }} onChange={event => {
          const value = event.target.value;
          if (value === 'custom') { setCustomOpen(true); return; }
          const next = value === 'today' ? { startDate: today, endDate: today } : presets.find(preset => preset.days === 365)!.filters;
          setCustomOpen(false); onDraftChange(next); onApply(next);
        }}>
        <MenuItem value="more" disabled>Ещё</MenuItem><MenuItem value="today">Сегодня</MenuItem><MenuItem value="365">Год</MenuItem><MenuItem value="custom">Свой период</MenuItem>
      </Select> : null}
    </Stack>
    {customOpen ? <Stack direction={{ xs: 'column', sm: 'row' }} gap={1} alignItems={{ sm: 'center' }}>
      <TextField id="period-start" label="С даты" type="date" size="small" value={draftFilters.startDate ?? ''}
        onChange={(event) => onDraftChange({ ...draftFilters, startDate: event.target.value })}
        InputLabelProps={{ shrink: true }} inputProps={{ max: today }} sx={{ maxWidth: { sm: 190 } }} />
      <TextField label="По дату" type="date" size="small" value={draftFilters.endDate ?? ''}
        onChange={(event) => onDraftChange({ ...draftFilters, endDate: event.target.value })}
        InputLabelProps={{ shrink: true }} inputProps={{ max: today }} sx={{ maxWidth: { sm: 190 } }} />
      <Button variant="contained" disabled={invalid} onClick={() => { onApply(draftFilters); setCustomOpen(false); }}>Показать</Button>
      {invalid && draftFilters.startDate && draftFilters.endDate ?
        <span role="alert" style={{ color: ui.status.error.color, fontSize: 12 }}>Выберите период не позднее сегодня и не длиннее 366 дней.</span> : null}
    </Stack> : null}
  </Stack>;
}

export function defaultPeriod(): DashboardFilters {
  const endDate = addDays(moscowToday(), -1);
  return { startDate: addDays(endDate, -6), endDate };
}

export function moscowToday(): string {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: 'Europe/Moscow', year: 'numeric', month: '2-digit', day: '2-digit'
  }).formatToParts(new Date());
  const date = Object.fromEntries(parts.map((part) => [part.type, part.value]));
  return `${date.year}-${date.month}-${date.day}`;
}

function addDays(isoDate: string, days: number): string {
  const date = new Date(`${isoDate}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}
