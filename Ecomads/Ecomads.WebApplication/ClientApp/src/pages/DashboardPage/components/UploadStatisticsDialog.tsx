import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, FormControl, FormControlLabel, FormLabel, Radio, RadioGroup, Stack, TextField, Typography } from '@mui/material';
import { useState } from 'react';
import type { DashboardUploadMode, UploadStatisticsRequest } from '../dashboardApi';

type UploadStatisticsDialogProps = {
  open: boolean;
  isUploading: boolean;
  error?: string | null;
  onClose: () => void;
  onSubmit: (request: UploadStatisticsRequest) => Promise<void> | void;
};

export function UploadStatisticsDialog({ open, isUploading, error, onClose, onSubmit }: UploadStatisticsDialogProps) {
  const [mode, setMode] = useState<DashboardUploadMode>('general');
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const [keywordsFile, setKeywordsFile] = useState<File | null>(null);
  const [localError, setLocalError] = useState<string | null>(null);

  const submit = async () => {
    setLocalError(null);

    if (!file || !startDate || !endDate) {
      setLocalError('Заполните период и выберите файл статистики.');
      return;
    }

    if (mode === 'with-keywords' && !keywordsFile) {
      setLocalError('Добавьте файл отчета по ключевым словам.');
      return;
    }

    await onSubmit({
      file,
      startDate,
      endDate,
      mode,
      keywordsFile
    });
  };

  const close = () => {
    if (!isUploading) {
      onClose();
    }
  };

  return (
    <Dialog open={open} fullWidth maxWidth="sm" onClose={close}>
      <DialogTitle>Загрузка статистики</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          <Typography color="text.secondary">
            Общая статистика — это отчет из Wildberries. Статистика по ключевым словам — отчет из Эвирмы.
            Если добавляете оба файла, выберите здесь тот же период, за который выгружали отчет Эвирмы.
          </Typography>

          {(localError || error) ? <Alert severity="error">{localError || error}</Alert> : null}

          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField
              InputLabelProps={{ shrink: true }}
              fullWidth
              label="С даты"
              type="date"
              value={startDate}
              onChange={(event) => setStartDate(event.target.value)}
            />
            <TextField
              InputLabelProps={{ shrink: true }}
              fullWidth
              label="По дату"
              type="date"
              value={endDate}
              onChange={(event) => setEndDate(event.target.value)}
            />
          </Stack>

          <TextField
            InputLabelProps={{ shrink: true }}
            fullWidth
            inputProps={{ accept: '.xlsx' }}
            label="Общая статистика из WB (.xlsx)"
            type="file"
            onChange={(event) => setFile((event.target as HTMLInputElement).files?.[0] ?? null)}
          />

          <FormControl>
            <FormLabel>Режим загрузки</FormLabel>
            <RadioGroup value={mode} onChange={(event) => setMode(event.target.value as DashboardUploadMode)}>
              <FormControlLabel value="general" control={<Radio />} label="Только общая статистика из WB" />
              <FormControlLabel value="with-keywords" control={<Radio />} label="Статистика из WB + ключевые слова из Эвирмы" />
            </RadioGroup>
          </FormControl>

          <TextField
            InputLabelProps={{ shrink: true }}
            disabled={mode !== 'with-keywords'}
            fullWidth
            inputProps={{ accept: '.xlsx' }}
            helperText={mode === 'with-keywords' ? 'Период отчета из Эвирмы должен совпадать с датами выше.' : undefined}
            label="Статистика по ключевым словам из Эвирмы (.xlsx)"
            type="file"
            onChange={(event) => setKeywordsFile((event.target as HTMLInputElement).files?.[0] ?? null)}
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button disabled={isUploading} onClick={close}>
          Отмена
        </Button>
        <Button disabled={isUploading} variant="contained" onClick={submit}>
          {isUploading ? 'Загружаем...' : 'Загрузить'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
