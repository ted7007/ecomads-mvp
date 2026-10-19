import { Accordion, AccordionDetails, AccordionSummary, Alert, Box, Button, Card, CardContent, Chip, FormControlLabel, Stack, Switch, Tooltip, Typography } from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { disconnectWbStore, getWbSyncOverview, loadWbYearHistory, refreshWbStore, setWbAutoRefresh, type WbStore } from './wbStoresApi';
import { SyncDashboard } from './SyncDashboard';
import { moscowTime, yearKinds } from './storesModel';
import { ConfirmDisconnect, SectionTitle } from './storesUi';
import { lightUiTokens as ui } from '../../app/lightUiTokens';
export const invalidateStoreData = (cache: ReturnType<typeof useQueryClient>) => Promise.all(['wb-stores', 'projects', 'campaign-summary', 'campaign-nomenclatures', 'campaign-daily', 'wb-clusters', 'wb-jam', 'wb-sync-overview', 'wb-sync-history', 'wb-sync-details', 'wb-data-state', 'wb-recommendation-summary', 'wb-cluster-recommendations', 'telegram-history'].map(key => cache.invalidateQueries({ queryKey: [key] })));
export function WbStorePanel({ store, onReplace }: { store: WbStore; onReplace: () => void }) {
  const cache = useQueryClient();
  const [confirm, setConfirm] = useState(false);
  const sync = useQuery({ queryKey: ['wb-sync-overview', store.id], queryFn: () => getWbSyncOverview(store.id), refetchInterval: q => q.state.data?.sources.some(s => s.activeJob) ? 15000 : 60000, refetchIntervalInBackground: false, refetchOnWindowFocus: true });
  const completedStamp = sync.data?.sources.map(s => `${s.kind}:${s.lastSuccessAtUtc ?? ''}`).sort().join('|');
  const previousStamp = useRef<string>();
  useEffect(() => {
    if (completedStamp === undefined) return;
    const changed = previousStamp.current !== undefined && previousStamp.current !== completedStamp;
    previousStamp.current = completedStamp;
    if (changed) void Promise.all(['wb-stores', 'projects', 'campaign-summary', 'campaign-nomenclatures', 'campaign-daily', 'wb-clusters', 'wb-jam', 'wb-recommendation-summary', 'wb-cluster-recommendations'].map(key => cache.invalidateQueries({ queryKey: [key] })));
  }, [completedStamp, cache]);
  const refresh = useMutation({ mutationFn: () => refreshWbStore(store.id), onSuccess: () => Promise.all(['wb-sync-overview', 'wb-sync-history'].map(key => cache.invalidateQueries({ queryKey: [key, store.id] }))) });
  const year = useMutation({ mutationFn: () => loadWbYearHistory(store.id), onSuccess: () => Promise.all(['wb-sync-overview', 'wb-sync-history'].map(key => cache.invalidateQueries({ queryKey: [key, store.id] }))) });
  const disconnect = useMutation({ mutationFn: () => disconnectWbStore(store.id), onSuccess: async () => { setConfirm(false); await invalidateStoreData(cache); } });
  const auto = useMutation({ mutationFn: (enabled: boolean) => setWbAutoRefresh(store.id, enabled), onSuccess: () => Promise.all(['wb-stores', 'wb-sync-overview'].map(key => cache.invalidateQueries({ queryKey: [key] }))) });
  const jobs = sync.data?.sources.flatMap(s => s.activeJob ? [s.activeJob] : []) ?? [];
  const blocked = store.autoRefreshBlocked || sync.data?.autoRefreshBlocked;
  const jam = { active: 'Доступен', unknown: 'Не подтверждён', access_denied: 'Нет доступа', payment_required: 'Требуется подписка / оплата' }[store.jamStatus];
  const unknown = <Tooltip title="Ответ сервиса пока не содержит этих сведений о сохранённом токене"><span>Сведения недоступны</span></Tooltip>;
  const properties = [['Тип токена', unknown], ['Категории', unknown], ['Доступ', unknown], ['Действует до', moscowTime(store.tokenExpiresAtUtc)], ['Подписка Джем', <Stack direction="row" gap={.5} flexWrap="wrap" key="jam"><Chip size="small" label="Джем" sx={ui.status.jam} /><Typography variant="body2">{jam}</Typography>{store.jamCheckedAtUtc ? <Typography variant="caption" color="text.secondary">Проверено {moscowTime(store.jamCheckedAtUtc)}</Typography> : null}</Stack>], ['Токен', `•••• ${store.tokenLastFour}`], ['Кампании', store.campaignCount.toLocaleString('ru-RU')]] as const;
  return <><Card component="section" aria-label="Подключение WB"><CardContent><Stack spacing={1.5}>
    <Stack direction="row" gap={1} useFlexGap flexWrap="wrap" alignItems="center"><SectionTitle>{store.name}</SectionTitle><Chip size="small" label={blocked ? 'Требует проверки' : 'Подключён'} sx={blocked ? ui.status.warning : ui.status.success} /><Stack direction="row" gap={.5} sx={{ ml: { sm: 'auto' } }}><Button size="small" variant="outlined" onClick={onReplace}>Заменить токен</Button><Button size="small" color="error" onClick={() => { disconnect.reset(); setConfirm(true); }}>Отключить кабинет</Button></Stack></Stack>
    {properties.map(([label, value]) => <Box key={label} sx={{ display: 'grid', gridTemplateColumns: 'minmax(100px, .7fr) minmax(0, 1.3fr)', gap: 1, borderTop: 1, borderColor: 'divider', pt: 1 }}><Typography variant="body2" color="text.secondary">{label}</Typography><Box sx={{ fontSize: 14, minWidth: 0, overflowWrap: 'anywhere' }}>{value}</Box></Box>)}
    {blocked ? <Alert severity="warning">Автосбор заблокирован. Проверьте или замените токен WB.</Alert> : null}
    <Accordion disableGutters elevation={0}><AccordionSummary expandIcon="⌄"><Typography variant="caption">Дополнительные сведения</Typography></AccordionSummary><AccordionDetails><Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>ID продавца WB: {store.externalId}</Typography><Typography variant="body2">Последнее обновление: {moscowTime(store.lastSyncAt)}</Typography></AccordionDetails></Accordion>
  </Stack></CardContent></Card>
    <ConfirmDisconnect open={confirm} title={`Отключить ${store.name}?`} description="Токен будет отключён, текущие загрузки остановятся. Сохранённая история останется." pending={disconnect.isPending} error={disconnect.isError ? <Alert severity="error">Не удалось отключить кабинет.</Alert> : undefined} onClose={() => setConfirm(false)} onConfirm={() => disconnect.mutate()} />
    <SyncDashboard storeId={store.id} overview={sync.data} loading={sync.isPending} error={sync.isError} refresh={() => void sync.refetch()} skipped={refresh.data?.skipped ?? []} controls={<Stack spacing={1}>
      <Stack direction={{ xs: 'column', sm: 'row' }} gap={1}><Button variant="contained" disabled={refresh.isPending || year.isPending || sync.isPending || sync.isError || jobs.some(j => ['fullstats', 'funnel_recent'].includes(j.kind))} onClick={() => refresh.mutate()}>Загрузить всё из WB</Button><Button variant="outlined" disabled={year.isPending || refresh.isPending || sync.isPending || sync.isError || jobs.some(j => yearKinds.has(j.kind))} onClick={() => year.mutate()}>Загрузить историю за год</Button></Stack>
      {refresh.isError || year.isError ? <Alert severity="error">{(refresh.error ?? year.error)?.message ?? 'Не удалось запустить сбор.'}</Alert> : null}
      <FormControlLabel label="Автоматически обновлять данные" control={<Switch checked={store.autoRefreshEnabled} disabled={auto.isPending || !store.autoRefreshAvailable || blocked} onChange={(_, enabled) => auto.mutate(enabled)} />} />
      {!store.autoRefreshAvailable ? <Typography variant="caption" color="text.secondary">Автосбор пока выключен на сервере.</Typography> : <Typography variant="caption" color="text.secondary">С 06:15 и 18:15 МСК{store.autoRefreshEnabled && sync.data?.nextAutoRefreshAtUtc ? ` · следующий запуск: ${moscowTime(sync.data.nextAutoRefreshAtUtc)}` : ''}</Typography>}
      {auto.isError ? <Alert severity="error">Не удалось изменить автосбор. Значение не сохранено.</Alert> : null}
    </Stack>} /></>;
}
