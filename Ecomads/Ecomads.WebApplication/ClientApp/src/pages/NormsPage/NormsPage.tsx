import { Accordion, AccordionDetails, AccordionSummary, Alert, Box, Button, Card, CardContent, Grid, MenuItem, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Fragment, useEffect, useState } from 'react';
import { getWbStores } from '../WbStoresPage/wbStoresApi';
import { getCampaignNormList, getCampaignNorms, getStoreCampaigns, getStoreNorms, saveCampaignNorms, saveStoreNorms } from './normsApi';
import type { CampaignNormOverrides, StoreNormValues } from './normsApi';
import { PaginationBar } from '../../shared/ui/PaginationBar';

const defaults: StoreNormValues = { targetDrr: 30, minClicks: 30, minSpend: 500,
  minOrders: 3, deviationPercent: 40, minCtr: 3 };
const emptyOverrides: CampaignNormOverrides = { customName: null, goal: null, targetDrr: null,
  minClicks: null, minSpend: null, minOrders: null, deviationPercent: null, minCtr: null };
const numericFields = [
  { key: 'targetDrr', label: 'Целевой рекламный ДРР, %' },
  { key: 'minCtr', label: 'Минимальный CTR, %' },
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
  const [rowsPerPage, setRowsPerPage] = useState(5);
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
  const storeDirty = Boolean(storeNorms.data) && JSON.stringify(storeValues) !== JSON.stringify(storeNorms.data?.values);
  const campaignDirty = Boolean(campaignNorms.data) && JSON.stringify(overrides) !== JSON.stringify(campaignNorms.data?.overrides);

  return <Stack spacing={2.5} sx={{ maxWidth: 1200 }}>
    <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" alignItems={{ md: 'center' }} gap={1.5}>
      <Box><Typography variant="h4" fontWeight={800}>Нормы</Typography>
        <Typography color="text.secondary">Пороги для блока «Требуют внимания» и оценки кластеров. Пустое поле кампании наследует норму кабинета.</Typography></Box>
      {storeId ? <Stack direction="row" alignItems="center" gap={1.5} flexShrink={0}>
        {storeDirty ? <Typography variant="caption" color="warning.main">Есть несохранённые изменения</Typography> : null}
        <Button variant="contained" disabled={!storeDirty || saveStore.isPending || storeNorms.isLoading}
          onClick={() => saveStore.mutate()}>Сохранить</Button>
      </Stack> : null}
    </Stack>
    {message ? <Alert severity={message.startsWith('Не удалось') ? 'error' : 'success'} onClose={() => setMessage('')}>{message}</Alert> : null}
    {stores.isError ? <Alert severity="error">Не удалось загрузить кабинеты WB.</Alert> : null}
    {stores.data?.length === 0 ? <Alert severity="info">Сначала подключите кабинет WB.</Alert> : null}
    {stores.data && stores.data.length > 1 ? <TextField select label="Кабинет WB" size="small" value={storeId}
      onChange={(event) => setStoreId(event.target.value)} sx={{ maxWidth: 300 }}>
      {stores.data.map((store) => <MenuItem key={store.id} value={store.id}>{store.name || 'Кабинет WB'}</MenuItem>)}
    </TextField> : null}

    {storeId ? <Grid container spacing={2} alignItems="stretch">
      <Grid item xs={12} md={4}><Card sx={{ height: '100%' }}><CardContent><Stack spacing={2}>
      <Typography variant="h6" fontWeight={800}>Нормы кабинета</Typography>
      <Typography variant="body2" color="text.secondary">Целевой рекламный ДРР применяется по умолчанию ко всем кампаниям.</Typography>
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} useFlexGap flexWrap="wrap">
        {numericFields.slice(0, 2).map(({ key, label }) => <TextField key={key} label={label} type="number" size="small"
          inputProps={{ min: key === 'minCtr' ? 0.01 : 0, max: key === 'minCtr' ? 100 : undefined, step: 0.01 }} value={storeValues[key]}
          onChange={(event) => setStoreValues((previous) => ({ ...previous, [key]: Number(event.target.value) }))} />)}
      </Stack>
    </Stack></CardContent></Card></Grid>
      <Grid item xs={12} md={8}><Card sx={{ height: '100%' }}><CardContent><Stack spacing={2}>
      <Typography variant="h6" fontWeight={800}>Достаточность данных</Typography>
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} useFlexGap flexWrap="wrap">
        {numericFields.slice(2).map(({ key, label }) => <TextField key={key} label={label} type="number" size="small"
          inputProps={{ min: 0 }} value={storeValues[key]}
          onChange={(event) => setStoreValues((previous) => ({ ...previous, [key]: Number(event.target.value) }))} />)}
      </Stack>
    </Stack></CardContent></Card></Grid>
      <Grid item xs={12}><Accordion disableGutters elevation={0} sx={{ bgcolor: 'transparent', '&:before': { display: 'none' } }}><AccordionSummary expandIcon="⌄"><Typography variant="body2">Как рассчитываются нормы и оценки</Typography></AccordionSummary><AccordionDetails>
        <Typography variant="body2" color="text.secondary">Целевой ДРР относится к рекламной сумме заказов. Порог отклонения сравнивает расход завершённого дня со средним за предыдущие семь дней при полном покрытии. Кластер без заказов выделяется только после минимального расхода и числа кликов. WB не передаёт выручку кластера.</Typography>
      </AccordionDetails></Accordion></Grid>
    </Grid> : null}

    {storeId ? <Card><CardContent><Stack spacing={2}>
      <Typography variant="h6" fontWeight={800}>Нормы и цели кампаний</Typography>
      <Typography variant="body2" color="text.secondary">Нажмите строку для редактирования. Серым показано значение, унаследованное от кабинета.</Typography>
      <TableContainer sx={{ maxHeight: 470, display: { xs: 'none', md: 'block' } }}><Table stickyHeader size="small" aria-label="Нормы кампаний">
        <TableHead><TableRow><TableCell sx={{ width: '38%' }}>Кампания</TableCell><TableCell sx={{ width: '28%' }}>Цель</TableCell>
          <TableCell align="right" sx={{ width: '20%' }}>ДРР рекламы, %</TableCell><TableCell align="right" sx={{ width: '14%' }}>Действие</TableCell></TableRow></TableHead>
        <TableBody>{(normList.data ?? []).slice(page * rowsPerPage, (page + 1) * rowsPerPage).map((row) =>
          <Fragment key={row.campaignId}><TableRow hover selected={row.campaignId === campaignId} onClick={() => setCampaignId(row.campaignId)} sx={{ cursor: 'pointer' }}>
            <TableCell><Typography fontWeight={700}>{row.name}</Typography>{row.name !== row.wbName ?
              <Typography variant="caption" color="text.secondary">В WB: {row.wbName}</Typography> : null}</TableCell>
            <TableCell>{row.campaignId === campaignId ? <TextField size="small" fullWidth aria-label={`Цель кампании ${row.name}`}
              placeholder="Цель не задана" value={overrides.goal ?? ''} disabled={campaignNorms.isLoading}
              onClick={(event) => event.stopPropagation()}
              onChange={(event) => setOverrides((previous) => ({ ...previous, goal: event.target.value || null }))} /> : row.goal || '—'}</TableCell>
            <TableCell align="right" sx={{ color: row.isInherited ? 'text.secondary' : 'text.primary' }}>{row.campaignId === campaignId ?
              <TextField size="small" type="number" aria-label={`ДРР рекламы для ${row.name}`} inputProps={{ min: 0 }}
                placeholder={String(campaignNorms.data?.effective.targetDrr ?? storeValues.targetDrr)}
                value={overrides.targetDrr ?? ''} disabled={campaignNorms.isLoading}
                onClick={(event) => event.stopPropagation()}
                onChange={(event) => setOverrides((previous) => ({ ...previous, targetDrr: event.target.value === '' ? null : Number(event.target.value) }))}
                helperText={overrides.targetDrr === null ? 'Наследуется' : 'Переопределено'}
                sx={{ width: 125, '& .MuiFormHelperText-root': { mx: 0 } }} /> : <>{row.targetDrr}{row.isInherited ? ' · наследуется' : ''}</>}</TableCell>
            <TableCell align="right">{row.campaignId === campaignId ? <Button size="small" variant="contained"
              disabled={!campaignDirty || saveCampaign.isPending || campaignNorms.isLoading}
              onClick={(event) => { event.stopPropagation(); saveCampaign.mutate(); }}>Сохранить</Button> : null}</TableCell>
          </TableRow>
          {row.campaignId === campaignId ? <TableRow sx={{ display: { xs: 'none', md: 'table-row' } }}><TableCell colSpan={4} sx={{ bgcolor: 'rgba(0,122,255,.04)' }}><Stack spacing={1.5} sx={{ py: 1 }}>
            <Typography variant="caption" color="text.secondary">Дополнительные настройки кампании</Typography>
            <TextField label="Своё название" size="small" sx={{ maxWidth: 320 }} value={overrides.customName ?? ''} onChange={(event) => setOverrides((previous) => ({ ...previous, customName: event.target.value || null }))} />
            <Stack direction="row" useFlexGap flexWrap="wrap" gap={1.5}>{numericFields.slice(1).map(({ key, label }) => <TextField key={key} label={label} type="number" size="small" sx={{ width: 190 }} inputProps={{ min: key === 'minCtr' ? 0.01 : 0, max: key === 'minCtr' ? 100 : undefined }} value={overrides[key] ?? ''}
              onChange={(event) => setOverrides((previous) => ({ ...previous, [key]: event.target.value === '' ? null : Number(event.target.value) }))}
              helperText={overrides[key] === null ? `Наследуется: ${campaignNorms.data?.effective[key] ?? storeValues[key]}` : 'Переопределено'} />)}</Stack>
            {campaignDirty ? <Typography variant="caption" color="warning.main">Есть несохранённые изменения</Typography> : null}
            <Button sx={{ alignSelf: 'flex-start' }} onClick={() => setOverrides(emptyOverrides)}>Сбросить переопределения</Button>
          </Stack></TableCell></TableRow> : null}</Fragment>)}</TableBody>
      </Table></TableContainer>
      <Box sx={{ display: { xs: 'none', md: 'block' } }}><PaginationBar count={normList.data?.length ?? 0} page={page} rowsPerPage={rowsPerPage} onPageChange={setPage} onRowsPerPageChange={(size) => { setRowsPerPage(size); setPage(0); }} /></Box>
      <Box sx={{ borderTop: '1px solid rgba(30,30,60,.1)', pt: 2, display: { xs: 'block', md: 'none' } }}>
        <Typography variant="subtitle1" fontWeight={800} sx={{ mb: 1 }}>Редактировать кампанию</Typography>
      <TextField select label="Кампания" value={campaignId} onChange={(event) => setCampaignId(event.target.value)}
        fullWidth sx={{ maxWidth: 520, mb: campaignId ? 2 : 0 }}>
        <MenuItem value="">Выберите кампанию</MenuItem>
        {campaigns.data?.map((campaign) => <MenuItem key={campaign.id} value={campaign.id}>
          {campaign.name} · {campaign.wbCampaignId}</MenuItem>)}
      </TextField>
      {campaignId ? <>
        <Typography variant="body2" color="text.secondary">Пустое числовое поле наследует норму кабинета. Версия кампании: {campaignNorms.data?.version ?? 0}.</Typography>
        {campaignDirty ? <Typography variant="caption" color="warning.main">Есть несохранённые изменения</Typography> : null}
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
        <Button variant="contained" sx={{ alignSelf: 'flex-start' }} disabled={!campaignDirty || saveCampaign.isPending || campaignNorms.isLoading}
          onClick={() => saveCampaign.mutate()}>Сохранить настройки кампании</Button>
        <Button size="small" sx={{ ml: 1 }} onClick={() => setOverrides(emptyOverrides)}>Сбросить переопределения</Button>
      </> : null}
      </Box>
    </Stack></CardContent></Card> : null}
  </Stack>;
}
