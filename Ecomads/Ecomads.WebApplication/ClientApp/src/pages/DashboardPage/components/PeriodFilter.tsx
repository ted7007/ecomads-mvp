import { Button, Stack, TextField, ToggleButton, ToggleButtonGroup } from '@mui/material';
import { useState } from 'react';
import type { LoadedPeriod } from '../../../shared/api/apiTypes';
import type { DashboardFilters } from '../dashboardApi';

type Props = {
  draftFilters: DashboardFilters;
  periods: LoadedPeriod[];
  onDraftChange: (filters: DashboardFilters) => void;
  onApply: (filters: DashboardFilters) => void;
};

export function PeriodFilter({ draftFilters, onDraftChange, onApply }: Props) {
  const [customOpen, setCustomOpen] = useState(false);
  const end = moscowYesterday();
  const presets = [7, 14, 30, 365].map((days) => ({
    days, filters: { startDate: addDays(end, 1 - days), endDate: end }
  }));
  const selected = customOpen ? 'custom' : presets.find(({ filters }) => filters.startDate === draftFilters.startDate &&
    filters.endDate === draftFilters.endDate)?.days ?? 'custom';
  const invalid = !draftFilters.startDate || !draftFilters.endDate ||
    draftFilters.startDate > draftFilters.endDate || draftFilters.endDate > end ||
    (Date.parse(`${draftFilters.endDate}T00:00:00Z`) - Date.parse(`${draftFilters.startDate}T00:00:00Z`)) / 86400000 > 365;

  return <Stack spacing={1}>
    <Stack direction={{ xs: 'column', sm: 'row' }} gap={1} alignItems={{ sm: 'center' }} justifyContent="space-between">
      <ToggleButtonGroup exclusive size="small" value={selected} aria-label="Период статистики"
        onChange={(_, value) => {
          const days = Number(value);
          if (!Number.isFinite(days)) return;
          const next = presets.find((preset) => preset.days === days)?.filters;
          if (!next) return;
          setCustomOpen(false);
          onDraftChange(next);
          onApply(next);
        }} sx={{ bgcolor: 'rgba(118,118,140,.10)', borderRadius: '999px', p: .4,
          maxWidth: '100%', overflowX: 'auto',
          '& .MuiToggleButton-root': { border: 0, borderRadius: '999px!important', px: { xs: 1, sm: 2.5 },
            fontSize: { xs: 12, sm: 13 }, whiteSpace: 'nowrap', textTransform: 'none' } }}>
        {presets.map(({ days }) => <ToggleButton key={days} value={days}>{days === 365 ? 'Год' : `${days} дней`}</ToggleButton>)}
        <ToggleButton value="custom" onClick={() => setCustomOpen(true)}>Свой период</ToggleButton>
      </ToggleButtonGroup>
    </Stack>
    {customOpen ? <Stack direction={{ xs: 'column', sm: 'row' }} gap={1} alignItems={{ sm: 'center' }}>
      <TextField id="period-start" label="С даты" type="date" size="small" value={draftFilters.startDate ?? ''}
        onChange={(event) => onDraftChange({ ...draftFilters, startDate: event.target.value })}
        InputLabelProps={{ shrink: true }} inputProps={{ max: end }} sx={{ maxWidth: { sm: 190 } }} />
      <TextField label="По дату" type="date" size="small" value={draftFilters.endDate ?? ''}
        onChange={(event) => onDraftChange({ ...draftFilters, endDate: event.target.value })}
        InputLabelProps={{ shrink: true }} inputProps={{ max: end }} sx={{ maxWidth: { sm: 190 } }} />
      <Button variant="contained" disabled={invalid} onClick={() => { onApply(draftFilters); setCustomOpen(false); }}>Показать</Button>
      {invalid && draftFilters.startDate && draftFilters.endDate ?
        <span role="alert" style={{ color: '#b42318', fontSize: 12 }}>Период должен быть завершённым и не длиннее 366 дней.</span> : null}
    </Stack> : null}
  </Stack>;
}

export function defaultPeriod(): DashboardFilters {
  const endDate = moscowYesterday();
  return { startDate: addDays(endDate, -6), endDate };
}

function moscowYesterday(): string {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: 'Europe/Moscow', year: 'numeric', month: '2-digit', day: '2-digit'
  }).formatToParts(new Date());
  const date = Object.fromEntries(parts.map((part) => [part.type, part.value]));
  return addDays(`${date.year}-${date.month}-${date.day}`, -1);
}

function addDays(isoDate: string, days: number): string {
  const date = new Date(`${isoDate}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}
