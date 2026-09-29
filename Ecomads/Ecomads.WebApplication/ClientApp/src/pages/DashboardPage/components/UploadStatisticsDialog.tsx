import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField, Typography } from '@mui/material';
import { useState } from 'react';
import type { UploadStatisticsRequest } from '../dashboardApi';

type UploadStatisticsDialogProps = {
  open: boolean;
  isUploading: boolean;
  error?: string | null;
  onClose: () => void;
  onSubmit: (request: UploadStatisticsRequest) => Promise<void> | void;
};

export function UploadStatisticsDialog({ open, isUploading, error, onClose, onSubmit }: UploadStatisticsDialogProps) {
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [campaignNamesFile, setCampaignNamesFile] = useState<File | null>(null);
  const [wbStatisticsFiles, setWbStatisticsFiles] = useState<File[]>([]);
  const [evirmaFiles, setEvirmaFiles] = useState<File[]>([]);
  const [localError, setLocalError] = useState<string | null>(null);

  const submit = async () => {
    setLocalError(null);

    if (!campaignNamesFile || wbStatisticsFiles.length === 0 || evirmaFiles.length === 0 || !startDate || !endDate) {
      setLocalError('Заполните период и загрузите все три типа отчетов.');
      return;
    }

    await onSubmit({
      campaignNamesFile,
      wbStatisticsFiles,
      evirmaFiles,
      startDate,
      endDate
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
            Загрузите 3 отчета для анализа каждой рекламной кампании:<br />
            1. WB продвижение → Статистика → Скачать «Расширенную статистику».<br />
            2. WB продвижение → Статистика → «Название кампании» → Скачать «Расширенную статистику».<br />
            3. WB продвижение → Кампании → «Название кампании» → Evirma (нужно расширение) → «Статистика РК по ключевым фразам» за 1 неделю.
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
            label="WB: название кампаний (.xlsx)"
            type="file"
            onChange={(event) => setCampaignNamesFile((event.target as HTMLInputElement).files?.[0] ?? null)}
          />
          <TextField
            InputLabelProps={{ shrink: true }}
            fullWidth
            inputProps={{ accept: '.xlsx', multiple: true }}
            helperText="Можно выбрать несколько файлов — по одному на кампанию."
            label="WB: расширенная статистика кампаний (.xlsx)"
            type="file"
            onChange={(event) => setWbStatisticsFiles(Array.from((event.target as HTMLInputElement).files ?? []))}
          />
          <TextField
            InputLabelProps={{ shrink: true }} fullWidth inputProps={{ accept: '.xlsx', multiple: true }}
            helperText="Период должен совпадать с выбранными датами выше. Можно выбрать несколько файлов."
            label="Evirma: статистика РК по ключевым фразам (.xlsx)" type="file"
            onChange={(event) => setEvirmaFiles(Array.from((event.target as HTMLInputElement).files ?? []))}
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
