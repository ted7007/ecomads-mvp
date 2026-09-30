import { Accordion, AccordionDetails, AccordionSummary, Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Grid, Stack, TextField, Typography } from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { connectWbStore, disconnectWbStore, getWbStores, getWbSyncOverview, startWbClusterSync, startWbJamSync, startWbSync } from './wbStoresApi';
import type { WbStore } from './wbStoresApi';
import { createTelegramLink, disconnectTelegramChat, getTelegramChats, sendYesterdaySummary } from './telegramApi';
import { SyncDashboard } from './SyncDashboard';

function StoreCard({ store }: { store: WbStore }) {
  const [error, setError] = useState<string | null>(null);
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [campaignIds, setCampaignIds] = useState('');
  const queryClient = useQueryClient();
  const sync = useQuery({
    queryKey: ['wb-sync-overview', store.id],
    queryFn: () => getWbSyncOverview(store.id),
    refetchInterval: (query) => query.state.data?.activeJob ? 15000 : 60000,
    refetchIntervalInBackground: false,
    refetchOnWindowFocus: true
  });
  useEffect(() => {
    if (sync.data?.sources.some((source) => source.lastJob?.status === 'completed')) {
      void queryClient.invalidateQueries({ queryKey: ['wb-stores'] });
      void queryClient.invalidateQueries({ queryKey: ['projects'] });
      void queryClient.invalidateQueries({ queryKey: ['wb-clusters'] });
      void queryClient.invalidateQueries({ queryKey: ['wb-jam'] });
    }
  }, [queryClient, sync.data?.sources]);
  const start = useMutation({
    mutationFn: () => startWbSync(store.id, {
      startDate: startDate || undefined,
      endDate: endDate || undefined,
      campaignIds: campaignIds.trim() ? campaignIds.split(',').map((id) => Number(id.trim())) : undefined
    }),
    onSuccess: async () => {
      setError(null);
      await queryClient.invalidateQueries({ queryKey: ['wb-sync-overview', store.id] });
      await queryClient.invalidateQueries({ queryKey: ['wb-sync-history', store.id] });
    },
    onError: (reason) => setError(reason instanceof Error ? reason.message : 'Не удалось запустить сбор')
  });
  const startClusters = useMutation({
    mutationFn: () => startWbClusterSync(store.id, {
      startDate: startDate || undefined,
      endDate: endDate || undefined,
      campaignIds: campaignIds.trim() ? campaignIds.split(',').map((id) => Number(id.trim())) : undefined
    }),
    onSuccess: async () => {
      setError(null);
      await queryClient.invalidateQueries({ queryKey: ['wb-sync-overview', store.id] });
      await queryClient.invalidateQueries({ queryKey: ['wb-sync-history', store.id] });
    },
    onError: (reason) => setError(reason instanceof Error ? reason.message : 'Не удалось запустить сбор кластеров')
  });
  const startJam = useMutation({
    mutationFn: () => startWbJamSync(store.id, {
      startDate: startDate || undefined,
      endDate: endDate || undefined,
      campaignIds: campaignIds.trim() ? campaignIds.split(',').map((id) => Number(id.trim())) : undefined
    }),
    onSuccess: async () => {
      setError(null);
      await queryClient.invalidateQueries({ queryKey: ['wb-sync-overview', store.id] });
      await queryClient.invalidateQueries({ queryKey: ['wb-sync-history', store.id] });
    },
    onError: (reason) => setError(reason instanceof Error ? reason.message : 'Не удалось запустить отчёт Джема')
  });
  const disconnect = useMutation({
    mutationFn: () => disconnectWbStore(store.id),
    onSuccess: async () => queryClient.invalidateQueries({ queryKey: ['wb-stores'] }),
    onError: (reason) => setError(reason instanceof Error ? reason.message : 'Не удалось отключить кабинет WB')
  });
  const invalidParameters = Boolean(startDate) !== Boolean(endDate) || Boolean(campaignIds.trim()) && !/^\d+(\s*,\s*\d+)*$/.test(campaignIds.trim());

  return (
    <Card>
      <CardContent>
        <Stack spacing={1.5}>
          <Stack direction="row" gap={1} alignItems="center" flexWrap="wrap">
            <Typography variant="h6" fontWeight={800}>{store.name}</Typography>
            <Chip label="Подключён" color="success" size="small" variant="outlined" />
          </Stack>
          <Typography variant="body2" color="text.secondary">Кампаний: {store.campaignCount} · токен действует до {new Date(store.tokenExpiresAtUtc).toLocaleDateString('ru-RU')}</Typography>
          <SyncDashboard storeId={store.id} overview={sync.data} refresh={() => { void sync.refetch(); }} error={sync.isError}
            onStart={(kind) => { if (kind === 'clusters') startClusters.mutate(); else if (kind === 'jam') startJam.mutate(); else start.mutate(); }}
            startPending={start.isPending || startClusters.isPending || startJam.isPending} startDisabledReason={invalidParameters ? 'Исправьте период или список ID кампаний в параметрах загрузки.' : null} />
          {error ? <Alert severity="error">{error}</Alert> : null}
          <Accordion disableGutters elevation={0} sx={{ border: '1px solid', borderColor: 'divider', borderRadius: '10px !important', '&:before': { display: 'none' } }}>
            <AccordionSummary expandIcon="⌄"><Typography fontWeight={700}>Параметры новой загрузки</Typography></AccordionSummary>
            <AccordionDetails><Stack spacing={1.5}>
          <Typography variant="body2" color="text.secondary">Без настройки: статистика за 30 завершённых дней, кластеры и Джем за 7 дней. Можно указать обе даты и ID кампаний.</Typography>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
            <TextField label="С даты" type="date" value={startDate} onChange={(event) => setStartDate(event.target.value)}
              slotProps={{ inputLabel: { shrink: true } }} size="small" />
            <TextField label="По дату" type="date" value={endDate} onChange={(event) => setEndDate(event.target.value)}
              slotProps={{ inputLabel: { shrink: true } }} size="small" />
          </Stack>
          {Boolean(startDate) !== Boolean(endDate) ? <Typography variant="caption" color="error">Укажите обе даты или оставьте оба поля пустыми.</Typography> : null}
          <TextField label="ID кампаний через запятую (необязательно)" size="small" value={campaignIds}
            onChange={(event) => setCampaignIds(event.target.value)}
            helperText="Можно выбрать завершённые кампании. Пустое поле — все активные и приостановленные." />
            </Stack></AccordionDetails>
          </Accordion>
          <Accordion disableGutters elevation={0} sx={{ border: '1px solid', borderColor: 'divider', borderRadius: '10px !important', '&:before': { display: 'none' } }}>
            <AccordionSummary expandIcon="⌄"><Typography fontWeight={700}>Настройки подключения</Typography></AccordionSummary>
            <AccordionDetails><Stack spacing={1} alignItems="flex-start">
              {store.name === 'Кабинет WB' ? <Typography variant="body2">ID продавца WB: {store.externalId}</Typography> : null}
              <Typography variant="body2">Токен: ••••{store.tokenLastFour}</Typography>
              <Typography variant="body2">Джем: {store.jamStatus === 'active' ? 'доступен' : store.jamStatus === 'access_denied' ? 'WB отказал в доступе; проверьте права и подписку' : 'доступ ещё не подтверждён'}</Typography>
              <Button color="error" variant="outlined" disabled={disconnect.isPending} onClick={() => disconnect.mutate()}>Отключить токен</Button>
            </Stack></AccordionDetails>
          </Accordion>
        </Stack>
      </CardContent>
    </Card>
  );
}

export function WbStoresPage() {
  const [token, setToken] = useState('');
  const [showConnection, setShowConnection] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const queryClient = useQueryClient();
  const stores = useQuery({ queryKey: ['wb-stores'], queryFn: getWbStores });
  const telegram = useQuery({ queryKey: ['telegram-chats'], queryFn: getTelegramChats });
  const link = useMutation({ mutationFn: createTelegramLink, onSuccess: () => setError(null),
    onError: (reason) => setError(reason instanceof Error ? reason.message : 'Не удалось создать ссылку') });
  const sendSummary = useMutation({ mutationFn: (storeId: string) => sendYesterdaySummary(storeId),
    onError: (reason) => setError(reason instanceof Error ? reason.message : 'Не удалось отправить сводку') });
  const disconnectChat = useMutation({ mutationFn: disconnectTelegramChat,
    onSuccess: async () => queryClient.invalidateQueries({ queryKey: ['telegram-chats'] }),
    onError: (reason) => setError(reason instanceof Error ? reason.message : 'Не удалось отключить чат') });
  const connect = useMutation({
    mutationFn: connectWbStore,
    onSuccess: async () => {
      setToken('');
      setShowConnection(false);
      setError(null);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['wb-stores'] }),
        queryClient.invalidateQueries({ queryKey: ['projects'] })
      ]);
    },
    onError: (reason) => setError(reason instanceof Error ? reason.message : 'Не удалось подключить кабинет WB')
  });

  return (
    <Stack spacing={3} sx={{ maxWidth: 1200 }}>
      <Box>
        <Typography variant="h4" fontWeight={800}>Кабинет WB и Telegram</Typography>
        <Typography color="text.secondary">Подключение, ручной сбор и доставка сводки.</Typography>
      </Box>

      {stores.isError ? <Alert severity="error">Не удалось загрузить список кабинетов.</Alert> : null}
      <Grid container spacing={2} alignItems="flex-start">
      <Grid item xs={12} lg={7}><Stack spacing={2}>
        {stores.data?.map((store) => <StoreCard key={store.id} store={store} />)}
      </Stack></Grid>
      <Grid item xs={12} lg={5}><Stack spacing={2}>
      <Card>
        <CardContent>
          <Stack spacing={2}>
            <Typography variant="h6" fontWeight={800}>Telegram</Typography>
            {telegram.isError ? <Alert severity="error">Не удалось загрузить привязанные чаты.</Alert> : null}
            {telegram.data && !telegram.data.botConfigured ? <Alert severity="info">Бот ещё не настроен. Привязка и отправка сводки станут доступны после добавления токена бота.</Alert> : null}
            {telegram.data?.chats.map((chat) => <Stack key={chat.id} direction={{ xs: 'column', sm: 'row' }} spacing={1} alignItems={{ sm: 'center' }}>
              <Typography sx={{ flex: 1 }}>{chat.displayName || 'Личный чат'} · с {new Date(chat.linkedAtUtc).toLocaleDateString('ru-RU')}</Typography>
              <Button color="error" size="small" disabled={disconnectChat.isPending} onClick={() => disconnectChat.mutate(chat.id)}>Отключить</Button>
            </Stack>)}
            {telegram.data?.chats.length === 0 && telegram.data.botConfigured ? <Typography color="text.secondary">Чаты пока не привязаны.</Typography> : null}
            <Button variant="outlined" disabled={!telegram.data?.botConfigured || link.isPending} onClick={() => link.mutate()} sx={{ alignSelf: 'flex-start' }}>Создать ссылку для привязки</Button>
            {stores.data?.[0] ? <Button variant="outlined" disabled={!telegram.data?.botConfigured || sendSummary.isPending} onClick={() => sendSummary.mutate(stores.data[0].id)} sx={{ alignSelf: 'flex-start' }}>Отправить вчерашнюю сводку</Button> : null}
            {sendSummary.data ? <Alert severity={sendSummary.data.failed ? 'warning' : 'success'}>Отправлено: {sendSummary.data.sent}; ошибок: {sendSummary.data.failed}.</Alert> : null}
            {link.data?.linkUrl ? <Button href={link.data.linkUrl} target="_blank" rel="noopener noreferrer" sx={{ alignSelf: 'flex-start' }}>Открыть бота и привязать чат</Button> : null}
            {link.data && !link.data.linkUrl ? <Typography>Отправьте боту команду /start {link.data.code}</Typography> : null}
            {link.data ? <Typography variant="caption">Ссылка действует до {new Date(link.data.expiresAtUtc).toLocaleString('ru-RU')}. После привязки обновите страницу.</Typography> : null}
          </Stack>
        </CardContent>
      </Card>
      <Button variant="outlined" onClick={() => setShowConnection(!showConnection)} sx={{ alignSelf: 'flex-start' }}>
        {stores.data?.length ? 'Подключить ещё кабинет или заменить токен' : 'Подключить кабинет WB'}
      </Button>
      {(showConnection || stores.data?.length === 0) ? <Card>
        <CardContent>
          <Stack spacing={2}>
            <Typography variant="h6" fontWeight={800}>{stores.data?.length ? 'Добавить или заменить токен' : 'Подключить кабинет'}</Typography>
            <Typography variant="body2" color="text.secondary">
              В личном кабинете WB создайте отдельный базовый токен с категорией «Продвижение» и доступом «Только чтение».
              Для общего ДРР дополнительно понадобится категория «Аналитика».
            </Typography>
            <TextField
              label="Токен WB"
              type="password"
              autoComplete="off"
              fullWidth
              value={token}
              onChange={(event) => setToken(event.target.value)}
              disabled={connect.isPending}
            />
            {error ? <Alert severity="error">{error}</Alert> : null}
            <Button
              variant="contained"
              disabled={!token.trim() || connect.isPending}
              onClick={() => connect.mutate(token.trim())}
              sx={{ alignSelf: 'flex-start' }}
            >
              {connect.isPending ? <CircularProgress size={20} color="inherit" /> : 'Проверить и подключить'}
            </Button>
          </Stack>
        </CardContent>
      </Card> : null}
      <Accordion disableGutters elevation={0} sx={{ bgcolor: 'transparent', '&:before': { display: 'none' } }}>
        <AccordionSummary expandIcon="⌄"><Typography variant="body2">Ограничения и подробности</Typography></AccordionSummary>
        <AccordionDetails><Typography variant="body2" color="text.secondary">Загрузка может ждать лимит WB. Ночной запуск, бюджетные уведомления и расписание утренней сводки пока не подключены.</Typography></AccordionDetails>
      </Accordion>
      </Stack></Grid>
      </Grid>
    </Stack>
  );
}
