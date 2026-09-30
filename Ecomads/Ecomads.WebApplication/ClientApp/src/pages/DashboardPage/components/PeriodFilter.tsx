import { Box, Button, Stack, TextField, ToggleButton, ToggleButtonGroup, Typography } from '@mui/material';
import type { LoadedPeriod } from '../../../shared/api/apiTypes';
import type { DashboardFilters } from '../dashboardApi';

type Props = {
  draftFilters: DashboardFilters;
  periods: LoadedPeriod[];
  onDraftChange: (filters: DashboardFilters) => void;
  onApply: (filters: DashboardFilters) => void;
};

export function PeriodFilter({ draftFilters, periods, onDraftChange, onApply }: Props) {
  const end = moscowYesterday();
  const presets = [7, 14, 30].map((days) => ({
    days, filters: { startDate: addDays(end, 1 - days), endDate: end }
  }));
  const selected = presets.find(({ filters }) => filters.startDate === draftFilters.startDate &&
    filters.endDate === draftFilters.endDate)?.days ?? 'custom';
  const invalid = !draftFilters.startDate || !draftFilters.endDate ||
    draftFilters.startDate > draftFilters.endDate || draftFilters.endDate > end;

  return <Stack spacing={1.5}>
    <Stack direction={{ xs: 'column', md: 'row' }} gap={1.5} alignItems={{ md: 'center' }} justifyContent="space-between">
      <Box>
        <Typography variant="subtitle2" fontWeight={700}>Период статистики</Typography>
        <Typography variant="caption" color="text.secondary">Завершённые дни по московскому времени</Typography>
      </Box>
      <ToggleButtonGroup exclusive size="small" value={selected} aria-label="Период статистики"
        onChange={(_, value) => {
          const days = Number(value);
          if (!Number.isFinite(days)) return;
          const next = presets.find((preset) => preset.days === days)?.filters;
          if (!next) return;
          onDraftChange(next);
          onApply(next);
        }} sx={{ bgcolor: 'rgba(118,118,140,.10)', borderRadius: '999px', p: .4,
          '& .MuiToggleButton-root': { border: 0, borderRadius: '999px!important', px: 2.5, textTransform: 'none' } }}>
        {presets.map(({ days }) => <ToggleButton key={days} value={days}>{days} дней</ToggleButton>)}
        <ToggleButton value="custom" onClick={() => document.getElementById('period-start')?.focus()}>Свой</ToggleButton>
      </ToggleButtonGroup>
    </Stack>
    <Stack direction={{ xs: 'column', sm: 'row' }} gap={1} alignItems={{ sm: 'center' }}>
      <TextField id="period-start" label="С даты" type="date" size="small" value={draftFilters.startDate ?? ''}
        onChange={(event) => onDraftChange({ ...draftFilters, startDate: event.target.value })}
        InputLabelProps={{ shrink: true }} inputProps={{ max: end }} sx={{ maxWidth: { sm: 190 } }} />
      <TextField label="По дату" type="date" size="small" value={draftFilters.endDate ?? ''}
        onChange={(event) => onDraftChange({ ...draftFilters, endDate: event.target.value })}
        InputLabelProps={{ shrink: true }} inputProps={{ max: end }} sx={{ maxWidth: { sm: 190 } }} />
      <Button variant="contained" disabled={invalid} onClick={() => onApply(draftFilters)}>Показать</Button>
      <Typography variant="caption" color="text.secondary" sx={{ ml: { sm: 'auto' } }}>
        {periods.length ? `Всего дней в истории: ${periods.length}` : 'Данные появятся после загрузки WB'}
      </Typography>
    </Stack>
  </Stack>;
}

export function defaultPeriod(): DashboardFilters {
  const endDate = moscowYesterday();
  return { startDate: addDays(endDate, -29), endDate };
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
