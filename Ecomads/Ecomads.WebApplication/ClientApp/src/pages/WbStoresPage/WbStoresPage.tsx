import { Alert, Box, Button, MenuItem, Stack, TextField } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { getWbStores } from './wbStoresApi';
import { WbStorePanel } from './WbStorePanel';
import { TokenConnectionCard } from './TokenConnectionCard';
import { TelegramCard } from './TelegramCard';
import { PageHeader } from '../../shared/ui/PageHeader';
import { LoadingState } from '../../shared/ui/LoadingState';
export function WbStoresPage() {
  const stores = useQuery({ queryKey: ['wb-stores'], queryFn: getWbStores });
  const [selected, setSelected] = useState(''); const [replacing, setReplacing] = useState<string>();
  const store = stores.data?.find(s => s.id === selected) ?? stores.data?.[0];
  return <Stack spacing={2.5}><PageHeader title="Кабинет WB и Telegram" />
    {(stores.data?.length ?? 0) > 1 ? <TextField select label="Кабинет WB" size="small" value={store?.id ?? ''} onChange={e => { setSelected(e.target.value); setReplacing(undefined); }} sx={{ maxWidth: 420 }}>{stores.data?.map(s => <MenuItem key={s.id} value={s.id}>{s.name}</MenuItem>)}</TextField> : null}
    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0,1fr)', lg: 'minmax(0,7fr) minmax(0,5fr)' }, gap: 2, alignItems: 'start' }}>
      <Stack spacing={2} sx={{ minWidth: 0 }}>
        {stores.isPending ? <LoadingState title="Загружаем кабинеты" /> : null}
        {stores.isError ? <Alert severity="error">Не удалось загрузить кабинеты. <Button onClick={() => void stores.refetch()}>Повторить</Button></Alert> : null}
        {store ? <WbStorePanel key={store.id} store={store} onReplace={() => setReplacing(store.id)} /> : null}
        {(stores.data || !stores.isPending && !stores.isError) ? <TokenConnectionCard key={`${store?.id ?? 'empty'}:${replacing ?? 'new'}`} hasStores={!!stores.data?.length} replacing={replacing === store?.id ? store?.name : undefined} onCancel={() => setReplacing(undefined)} onConnected={id => { setSelected(id); setReplacing(undefined); }} /> : null}
      </Stack><TelegramCard store={store} storesLoading={stores.isPending || stores.isError && !stores.data} />
    </Box></Stack>;
}
