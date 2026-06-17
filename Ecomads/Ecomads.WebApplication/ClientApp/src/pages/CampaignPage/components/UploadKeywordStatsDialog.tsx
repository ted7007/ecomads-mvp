import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField } from '@mui/material';
import { useState } from 'react';
import type { UploadKeywordStatsRequest } from '../campaignApi';

type UploadKeywordStatsDialogProps = {
  campaignId: string;
  open: boolean;
  isUploading: boolean;
  error?: string | null;
  onClose: () => void;
  onSubmit: (request: UploadKeywordStatsRequest) => Promise<void> | void;
};

export function UploadKeywordStatsDialog({ campaignId, open, isUploading, error, onClose, onSubmit }: UploadKeywordStatsDialogProps) {
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const [localError, setLocalError] = useState<string | null>(null);

  const submit = async () => {
    setLocalError(null);

    if (!file || !startDate || !endDate) {
      setLocalError('Заполните период и выберите файл ключевых слов.');
      return;
    }

    await onSubmit({ campaignId, file, startDate, endDate });
  };

  return (
    <Dialog open={open} fullWidth maxWidth="sm" onClose={isUploading ? undefined : onClose}>
      <DialogTitle>Загрузка ключевых слов</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          {(localError || error) ? <Alert severity="error">{localError || error}</Alert> : null}
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField InputLabelProps={{ shrink: true }} fullWidth label="С даты" type="date" value={startDate} onChange={(event) => setStartDate(event.target.value)} />
            <TextField InputLabelProps={{ shrink: true }} fullWidth label="По дату" type="date" value={endDate} onChange={(event) => setEndDate(event.target.value)} />
          </Stack>
          <TextField
            InputLabelProps={{ shrink: true }}
            fullWidth
            inputProps={{ accept: '.xlsx' }}
            label="Файл статистики по ключевым словам (.xlsx)"
            type="file"
            onChange={(event) => setFile((event.target as HTMLInputElement).files?.[0] ?? null)}
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button disabled={isUploading} onClick={onClose}>
          Отмена
        </Button>
        <Button disabled={isUploading} variant="contained" onClick={submit}>
          {isUploading ? 'Загружаем...' : 'Загрузить'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

