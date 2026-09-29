import { Alert, Button, Card, CardContent, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { getWbStores } from '../WbStoresPage/wbStoresApi';
import { getCampaignNorms, getStoreCampaigns, getStoreNorms, saveCampaignNorms, saveStoreNorms } from './normsApi';
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
  const queryClient = useQueryClient();
  const stores = useQuery({ queryKey: ['wb-stores'], queryFn: getWbStores });
  const campaigns = useQuery({ queryKey: ['wb-store-campaigns', storeId],
    queryFn: () => getStoreCampaigns(storeId), enabled: Boolean(storeId) });
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
        queryClient.invalidateQueries({ queryKey: ['wb-clusters', campaignId] })
      ]); },
    onError: () => setMessage('Не удалось сохранить настройки кампании.') });

  return <Stack spacing={3} sx={{ maxWidth: 960 }}>
    <div><Typography variant="h4" fontWeight={700}>Нормы</Typography>
      <Typography color="text.secondary">Общие пороги кабинета и индивидуальные настройки кампании.</Typography></div>
    {message ? <Alert severity={message.startsWith('Не удалось') ? 'error' : 'success'} onClose={() => setMessage('')}>{message}</Alert> : null}
    {stores.isError ? <Alert severity="error">Не удалось загрузить кабинеты WB.</Alert> : null}
    {stores.data?.length === 0 ? <Alert severity="info">Сначала подключите кабинет WB.</Alert> : null}
    {stores.data?.length ? <TextField select label="Кабинет WB" value={storeId} onChange={(event) => setStoreId(event.target.value)}>
      {stores.data.map((store) => <MenuItem key={store.id} value={store.id}>{store.name} · {store.externalId}</MenuItem>)}
    </TextField> : null}

    {storeId ? <Card><CardContent><Stack spacing={2}>
      <Typography variant="h6">Нормы кабинета</Typography>
      <Typography variant="body2" color="text.secondary">Версия: {storeNorms.data?.version ?? 0}. Значения применяются как умолчания для кампаний.</Typography>
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} useFlexGap flexWrap="wrap">
        {numericFields.map(({ key, label }) => <TextField key={key} label={label} type="number" size="small"
          inputProps={{ min: 0 }} value={storeValues[key]}
          onChange={(event) => setStoreValues((previous) => ({ ...previous, [key]: Number(event.target.value) }))} />)}
      </Stack>
      <Button variant="contained" sx={{ alignSelf: 'flex-start' }} disabled={saveStore.isPending || storeNorms.isLoading}
        onClick={() => saveStore.mutate()}>Сохранить нормы кабинета</Button>
    </Stack></CardContent></Card> : null}

    {storeId ? <Card><CardContent><Stack spacing={2}>
      <Typography variant="h6">Настройки кампании</Typography>
      <TextField select label="Кампания" value={campaignId} onChange={(event) => setCampaignId(event.target.value)}>
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
    </Stack></CardContent></Card> : null}
  </Stack>;
}
