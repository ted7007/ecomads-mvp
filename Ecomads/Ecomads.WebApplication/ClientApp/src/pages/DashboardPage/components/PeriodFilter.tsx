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

  return (
    <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} alignItems={{ xs: 'stretch', md: 'end' }}>
      <FormControl sx={{ minWidth: 220 }} size="small">
        <InputLabel id="dashboard-period-label">Загруженный период</InputLabel>
        <Select
          labelId="dashboard-period-label"
          label="Загруженный период"
          value={selectedPeriod}
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
          <MenuItem value="">Все периоды</MenuItem>
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
      <Button variant="contained" onClick={onApply}>
        Применить период
      </Button>
    </Stack>
  );
}

