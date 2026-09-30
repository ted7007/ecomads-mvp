import { Alert, Box, Button, Card, CardContent, Grid, MenuItem, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TablePagination, TableRow, TextField, Typography } from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { getWbStores } from '../WbStoresPage/wbStoresApi';
import { getCampaignNormList, getCampaignNorms, getStoreCampaigns, getStoreNorms, saveCampaignNorms, saveStoreNorms } from './normsApi';
import type { CampaignNormOverrides, StoreNormValues } from './normsApi';

const defaults: StoreNormValues = { targetDrr: 30, minClicks: 30, minSpend: 500,
  minOrders: 3, deviationPercent: 40 };
const emptyOverrides: CampaignNormOverrides = { customName: null, goal: null, targetDrr: null,
  minClicks: null, minSpend: null, minOrders: null, deviationPercent: null };
const numericFields = [
  { key: 'targetDrr', label: 'Целевой рекламный ДРР, %' },
  { key: 'minClicks', label: 'Минимум кликов' },
  { key: 'minSpend', label: 'Минимум расхода, ₽' },
  { key: 'minOrders', label: 'Минимум заказов' },
  { key: 'deviationPercent', label: 'Порог отклонения от среднего, %' }
] as const;

export function NormsPage() {
  const [storeId, setStoreId] = useState('');
  const [campaignId, setCampaignId] = useState('');
  const [storeValues, setStoreValues] = useState<StoreNormValues>(defaults);
  const [overrides, setOverrides] = useState<CampaignNormOverrides>(emptyOverrides);
  const [message, setMessage] = useState('');
  const [page, setPage] = useState(0);
  const [rowsPerPage, setRowsPerPage] = useState(10);
  const queryClient = useQueryClient();
  const stores = useQuery({ queryKey: ['wb-stores'], queryFn: getWbStores });
  const campaigns = useQuery({ queryKey: ['wb-store-campaigns', storeId],
    queryFn: () => getStoreCampaigns(storeId), enabled: Boolean(storeId) });
  const normList = useQuery({ queryKey: ['wb-campaign-norm-list', storeId],
    queryFn: () => getCampaignNormList(storeId), enabled: Boolean(storeId) });
  const storeNorms = useQuery({ queryKey: ['wb-store-norms', storeId],
    queryFn: () => getStoreNorms(storeId), enabled: Boolean(storeId) });
  const campaignNorms = useQuery({ queryKey: ['wb-campaign-norms', campaignId],
    queryFn: () => getCampaignNorms(campaignId), enabled: Boolean(campaignId) });

  useEffect(() => { if (!storeId && stores.data?.length) setStoreId(stores.data[0].id); }, [storeId, stores.data]);
  useEffect(() => { setCampaignId(''); }, [storeId]);
  useEffect(() => { if (storeNorms.data) setStoreValues(storeNorms.data.values); }, [storeNorms.data]);
  useEffect(() => { setOverrides(campaignNorms.data?.overrides ?? emptyOverrides); }, [campaignNorms.data, campaignId]);

  const saveStore = useMutation({ mutationFn: () => saveStoreNorms(storeId, storeValues),
    onSuccess: async () => { setMessage('Нормы кабинета сохранены.');
      await queryClient.invalidateQueries({ queryKey: ['wb-store-norms', storeId] }); },
    onError: () => setMessage('Не удалось сохранить нормы кабинета.') });
  const saveCampaign = useMutation({ mutationFn: () => saveCampaignNorms(campaignId, overrides),
    onSuccess: async () => { setMessage('Настройки кампании сохранены.');
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['wb-campaign-norms', campaignId] }),
        queryClient.invalidateQueries({ queryKey: ['wb-campaign-norm-list', storeId] }),
        queryClient.invalidateQueries({ queryKey: ['wb-clusters', campaignId] })
      ]); },
    onError: () => setMessage('Не удалось сохранить настройки кампании.') });

  return <Stack spacing={3} sx={{ maxWidth: 1200 }}>
    <div><Typography variant="h4" fontWeight={800}>Нормы</Typography>
      <Typography color="text.secondary">Пороги для блока «Требуют внимания» и оценки кластеров. Пустое поле кампании наследует норму кабинета.</Typography></div>
    {message ? <Alert severity={message.startsWith('Не удалось') ? 'error' : 'success'} onClose={() => setMessage('')}>{message}</Alert> : null}
    {stores.isError ? <Alert severity="error">Не удалось загрузить кабинеты WB.</Alert> : null}
    {stores.data?.length === 0 ? <Alert severity="info">Сначала подключите кабинет WB.</Alert> : null}
    {stores.data?.length ? <TextField select label="Кабинет WB" value={storeId} onChange={(event) => setStoreId(event.target.value)}>
      {stores.data.map((store) => <MenuItem key={store.id} value={store.id}>{store.name} · {store.externalId}</MenuItem>)}
    </TextField> : null}

    {storeId ? <Grid container spacing={2}>
      <Grid item xs={12} md={8}><Card sx={{ height: '100%' }}><CardContent><Stack spacing={2}>
      <Typography variant="h6" fontWeight={800}>Нормы кабинета</Typography>
      <Typography variant="body2" color="text.secondary">Версия: {storeNorms.data?.version ?? 0}. Значения применяются как умолчания для кампаний.</Typography>
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} useFlexGap flexWrap="wrap">
        {numericFields.map(({ key, label }) => <TextField key={key} label={label} type="number" size="small"
          inputProps={{ min: 0 }} value={storeValues[key]}
          onChange={(event) => setStoreValues((previous) => ({ ...previous, [key]: Number(event.target.value) }))} />)}
      </Stack>
      <Button variant="contained" sx={{ alignSelf: 'flex-start' }} disabled={saveStore.isPending || storeNorms.isLoading}
        onClick={() => saveStore.mutate()}>Сохранить нормы кабинета</Button>
    </Stack></CardContent></Card></Grid>
      <Grid item xs={12} md={4}><Card sx={{ height: '100%' }}><CardContent>
        <Typography variant="h6" fontWeight={800} sx={{ mb: 1 }}>Как считаем</Typography>
        <Typography variant="body2" color="text.secondary" paragraph>Целевой ДРР относится к рекламной сумме заказов. Для общего ДРР пока нет полного расхода товара во всех кампаниях.</Typography>
        <Typography variant="body2" color="text.secondary" paragraph>Порог отклонения сравнивает расход завершённого дня со средним за предыдущие семь дней при полном покрытии.</Typography>
        <Typography variant="body2" color="text.secondary">Кластер без заказов выделяется только после минимального расхода и числа кликов. Выручка кластера WB не передаётся.</Typography>
      </CardContent></Card></Grid>
    </Grid> : null}

    {storeId ? <Card><CardContent><Stack spacing={2}>
      <Typography variant="h6" fontWeight={800}>Нормы и цели кампаний</Typography>
      <Typography variant="body2" color="text.secondary">Нажмите строку для редактирования. Серым показано значение, унаследованное от кабинета.</Typography>
      <TableContainer sx={{ maxHeight: 470 }}><Table stickyHeader size="small" aria-label="Нормы кампаний">
        <TableHead><TableRow><TableCell>Кампания</TableCell><TableCell>Цель</TableCell><TableCell align="right">ДРР рекламы, %</TableCell></TableRow></TableHead>
        <TableBody>{(normList.data ?? []).slice(page * rowsPerPage, (page + 1) * rowsPerPage).map((row) =>
          <TableRow key={row.campaignId} hover selected={row.campaignId === campaignId} onClick={() => setCampaignId(row.campaignId)} sx={{ cursor: 'pointer' }}>
            <TableCell><Typography fontWeight={700}>{row.name}</Typography>{row.name !== row.wbName ?
              <Typography variant="caption" color="text.secondary">В WB: {row.wbName}</Typography> : null}</TableCell>
            <TableCell>{row.goal || '—'}</TableCell>
            <TableCell align="right" sx={{ color: row.isInherited ? 'text.secondary' : 'text.primary' }}>{row.targetDrr}</TableCell>
          </TableRow>)}</TableBody>
      </Table></TableContainer>
      <TablePagination component="div" count={normList.data?.length ?? 0} page={page} rowsPerPage={rowsPerPage}
        onPageChange={(_, nextPage) => setPage(nextPage)}
        onRowsPerPageChange={(event) => { setRowsPerPage(Number(event.target.value)); setPage(0); }}
        rowsPerPageOptions={[5, 10, 20, 50]} labelRowsPerPage="Строк на странице"
        labelDisplayedRows={({ from, to, count }) => `${from}–${to} из ${count}`} />
      <Box sx={{ borderTop: '1px solid rgba(30,30,60,.1)', pt: 2 }}>
        <Typography variant="subtitle1" fontWeight={800} sx={{ mb: 1 }}>Редактировать кампанию</Typography>
      <TextField select label="Кампания" value={campaignId} onChange={(event) => setCampaignId(event.target.value)}
        fullWidth sx={{ maxWidth: 520, mb: campaignId ? 2 : 0 }}>
        <MenuItem value="">Выберите кампанию</MenuItem>
        {campaigns.data?.map((campaign) => <MenuItem key={campaign.id} value={campaign.id}>
          {campaign.name} · {campaign.wbCampaignId}</MenuItem>)}
      </TextField>
      {campaignId ? <>
        <Typography variant="body2" color="text.secondary">Пустое числовое поле наследует норму кабинета. Версия кампании: {campaignNorms.data?.version ?? 0}.</Typography>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
          <TextField label="Своё название" value={overrides.customName ?? ''}
            onChange={(event) => setOverrides((previous) => ({ ...previous, customName: event.target.value || null }))} fullWidth />
          <TextField label="Цель кампании" value={overrides.goal ?? ''}
            onChange={(event) => setOverrides((previous) => ({ ...previous, goal: event.target.value || null }))} fullWidth />
        </Stack>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} useFlexGap flexWrap="wrap">
          {numericFields.map(({ key, label }) => <TextField key={key} label={label} type="number" size="small"
            inputProps={{ min: 0 }} value={overrides[key] ?? ''}
            onChange={(event) => setOverrides((previous) => ({ ...previous,
              [key]: event.target.value === '' ? null : Number(event.target.value) }))}
            helperText={`Наследуется: ${campaignNorms.data?.effective[key] ?? storeValues[key]}`} />)}
        </Stack>
        <Button variant="contained" sx={{ alignSelf: 'flex-start' }} disabled={saveCampaign.isPending || campaignNorms.isLoading}
          onClick={() => saveCampaign.mutate()}>Сохранить настройки кампании</Button>
      </> : null}
      </Box>
    </Stack></CardContent></Card> : null}
  </Stack>;
}
