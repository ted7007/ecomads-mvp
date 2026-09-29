import { Button, FormControl, InputLabel, MenuItem, Select, Stack, TextField } from '@mui/material';
import type { LoadedPeriod } from '../../../shared/api/apiTypes';
import { formatDateForInput } from '../../../shared/lib/formatDate';
import type { DashboardFilters } from '../dashboardApi';

type PeriodFilterProps = {
  draftFilters: DashboardFilters;
  periods: LoadedPeriod[];
  onDraftChange: (filters: DashboardFilters) => void;
  onApply: () => void;
};

export function PeriodFilter({ draftFilters, periods, onDraftChange, onApply }: PeriodFilterProps) {
  const selectedPeriod = draftFilters.startDate && draftFilters.endDate ? `${draftFilters.startDate}|${draftFilters.endDate}` : '';
  const yesterday = moscowYesterday();
  const presets = [
    { label: 'Вчера', startDate: yesterday, endDate: yesterday },
    { label: 'Последние 7 дней', startDate: addDays(yesterday, -6), endDate: yesterday },
    { label: 'Последние 30 дней', startDate: addDays(yesterday, -29), endDate: yesterday }
  ];
  const knownPeriod = presets.some((period) => `${period.startDate}|${period.endDate}` === selectedPeriod) ||
    periods.some((period) => `${formatDateForInput(period.startDate)}|${formatDateForInput(period.endDate)}` === selectedPeriod);

  return (
    <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} alignItems={{ xs: 'stretch', md: 'end' }}>
      <FormControl sx={{ minWidth: 220 }} size="small">
        <InputLabel id="dashboard-period-label">Загруженный период</InputLabel>
        <Select
          labelId="dashboard-period-label"
          label="Загруженный период"
          value={knownPeriod ? selectedPeriod : ''}
          onChange={(event) => {
            const value = event.target.value;

            if (!value) {
              onDraftChange({ startDate: '', endDate: '' });
              return;
            }

            const [startDate, endDate] = value.split('|');
            onDraftChange({ startDate, endDate });
          }}
        >
          <MenuItem value="">Свой период</MenuItem>
          {presets.map((period) => <MenuItem key={period.label} value={`${period.startDate}|${period.endDate}`}>
            {period.label}
          </MenuItem>)}
          {periods.map((period) => {
            const startDate = formatDateForInput(period.startDate);
            const endDate = formatDateForInput(period.endDate);
            const value = `${startDate}|${endDate}`;

            return (
              <MenuItem key={value} value={value}>
                {startDate} - {endDate}
              </MenuItem>
            );
          })}
        </Select>
      </FormControl>

      <TextField
        InputLabelProps={{ shrink: true }}
        label="С даты"
        size="small"
        type="date"
        value={draftFilters.startDate ?? ''}
        onChange={(event) => onDraftChange({ ...draftFilters, startDate: event.target.value })}
      />
      <TextField
        InputLabelProps={{ shrink: true }}
        label="По дату"
        size="small"
        type="date"
        value={draftFilters.endDate ?? ''}
        onChange={(event) => onDraftChange({ ...draftFilters, endDate: event.target.value })}
      />
      <Button variant="contained" disabled={Boolean(draftFilters.startDate) !== Boolean(draftFilters.endDate)} onClick={onApply}>
        Применить период
      </Button>
    </Stack>
  );
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

