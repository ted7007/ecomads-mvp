import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField } from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { getCampaignNorms, saveCampaignNorms } from '../../NormsPage/normsApi';
export function CampaignNormDialog({ id, open, onClose }: { id: string; open: boolean; onClose: () => void }) {
  const query = useQuery({ queryKey: ['wb-campaign-norms', id], queryFn: () => getCampaignNorms(id), enabled: open });
  const [draft, setDraft] = useState<string | null>(null);
  const cache = useQueryClient(); const value = draft ?? query.data?.overrides.targetDrr?.toString() ?? '';
  const numeric = value.trim() === '' ? null : Number(value.replace(',', '.')); const invalid = numeric !== null && (!Number.isFinite(numeric) || numeric < 0 || numeric > 1000);
  const save = useMutation({ mutationFn: () => { if (!query.data) throw Error('Нормы не загружены'); return saveCampaignNorms(id, { ...query.data.overrides, targetDrr: numeric }); }, onSuccess: async () => { await Promise.all(['wb-campaign-norms', 'wb-campaign-norm-list', 'wb-clusters', 'wb-recommendation-summary', 'wb-cluster-recommendations', 'campaign-summary', 'projects'].map(key => cache.invalidateQueries({ queryKey: [key] }))); onClose(); } });
  return <Dialog open={open} onClose={() => !save.isPending && onClose()} fullWidth maxWidth="xs"><DialogTitle>Норма ДРР кампании</DialogTitle><DialogContent><Stack spacing={2} sx={{ pt: 1 }}>{query.isPending ? 'Загрузка…' : query.isError ? <Alert severity="error">Не удалось загрузить нормы. <Button onClick={() => void query.refetch()}>Повторить</Button></Alert> : <TextField autoFocus label="Норма ДРР, %" value={value} onChange={e => setDraft(e.target.value)} error={invalid} helperText={invalid ? 'Укажите число от 0 до 1000' : 'Пустое поле — наследовать норму кабинета'} inputProps={{ inputMode: 'decimal' }} />}{save.isError ? <Alert severity="error">Не удалось сохранить норму.</Alert> : null}</Stack></DialogContent><DialogActions><Button disabled={save.isPending} onClick={onClose}>Отмена</Button><Button disabled={!query.data || invalid || save.isPending} onClick={() => save.mutate()}>Сохранить</Button></DialogActions></Dialog>;
}
