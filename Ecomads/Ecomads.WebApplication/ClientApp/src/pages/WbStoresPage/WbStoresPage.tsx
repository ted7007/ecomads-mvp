import { Accordion, AccordionDetails, AccordionSummary, Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Grid, Stack, TextField, Typography } from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { connectWbStore, disconnectWbStore, getWbStores, getWbSyncOverview, refreshWbStore } from './wbStoresApi';
import type { WbStore } from './wbStoresApi';
import { createTelegramLink, disconnectTelegramChat, getTelegramChats, sendYesterdaySummary } from './telegramApi';
import { SyncDashboard } from './SyncDashboard';

function StoreCard({ store }: { store: WbStore }) {
  const [error, setError] = useState<string | null>(null);
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
  const refreshAll = useMutation({
    mutationFn: () => refreshWbStore(store.id),
    onSuccess: async () => {
      setError(null);
      await queryClient.invalidateQueries({ queryKey: ['wb-sync-overview', store.id] });
      await queryClient.invalidateQueries({ queryKey: ['wb-sync-history', store.id] });
    },
    onError: (reason) => setError(reason instanceof Error ? reason.message : 'Не удалось запустить загрузку WB')
  });
  const disconnect = useMutation({
    mutationFn: () => disconnectWbStore(store.id),
    onSuccess: async () => queryClient.invalidateQueries({ queryKey: ['wb-stores'] }),
    onError: (reason) => setError(reason instanceof Error ? reason.message : 'Не удалось отключить кабинет WB')
  });
  const activeJobs = sync.data?.sources.flatMap((source) => source.activeJob ? [source.activeJob] : []) ?? [];
  const estimates = activeJobs.map((job) => job.estimatedCompletionAtUtc)
    .filter((value): value is string => Boolean(value)).sort();
  const completion = estimates[estimates.length - 1];
  const completionTime = completion ? new Intl.DateTimeFormat('ru-RU', { timeZone: 'Europe/Moscow',
    hour: '2-digit', minute: '2-digit' }).format(new Date(completion)) : null;

  return (
    <Card>
      <CardContent>
        <Stack spacing={1.5}>
          <Stack direction="row" gap={1} alignItems="center" flexWrap="wrap">
            <Typography variant="h6" fontWeight={800}>{store.name}</Typography>
            <Chip label="Подключён" color="success" size="small" variant="outlined" />
          </Stack>
          <Typography variant="body2" color="text.secondary">Кампаний: {store.campaignCount} · токен действует до {new Date(store.tokenExpiresAtUtc).toLocaleDateString('ru-RU')}</Typography>
          <Stack direction={{ xs: 'column', sm: 'row' }} alignItems={{ sm: 'center' }} gap={1.5}>
            <Button variant="contained" disabled={activeJobs.some((job) => job.kind === 'fullstats' || job.kind === 'funnel_recent') || refreshAll.isPending} onClick={() => refreshAll.mutate()}
              sx={{ width: { xs: '100%', sm: 'auto' } }}>Загрузить всё из WB</Button>
            {activeJobs.length > 0 ? <Typography variant="body2" color="text.secondary">
              Загрузка идёт{completionTime ? `, завершится примерно в ${completionTime} МСК` : ''}.
            </Typography> : null}
          </Stack>
          <Typography variant="body2" color="text.secondary">Период выбирается автоматически: статистика кампаний и расходы кабинета — 31 день, все заказы — 30 дней, кластеры и Джем — 7 дней. Заказы за последние 7 дней загружаются сразу, более ранние — постепенно в пределах лимита WB.</Typography>
          {store.autoRefreshEnabled ? <Typography variant="body2" color="text.secondary">Автообновление каждый день после 06:00 МСК.</Typography> : null}
          <SyncDashboard storeId={store.id} overview={sync.data} refresh={() => { void sync.refetch(); }} error={sync.isError}
            skipped={refreshAll.data?.skipped ?? []} />
          {error ? <Alert severity="error">{error}</Alert> : null}
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
        <Button variant="outlined" onClick={() => setShowConnection(!showConnection)} sx={{ alignSelf: 'flex-start' }}>
          {stores.data?.length ? 'Подключить ещё кабинет или заменить токен' : 'Подключить кабинет WB'}
        </Button>
        {(showConnection || stores.data?.length === 0) ? <Card>
          <CardContent><Stack spacing={2}>
            <Typography variant="h6" fontWeight={800}>{stores.data?.length ? 'Добавить или заменить токен' : 'Подключить кабинет'}</Typography>
            <Typography variant="body2" color="text.secondary">
              В личном кабинете WB создайте отдельный базовый токен с категорией «Продвижение» и доступом «Только чтение».
              Для общего ДРР дополнительно понадобится категория «Аналитика».
            </Typography>
            <TextField label="Токен WB" type="password" autoComplete="off" fullWidth value={token}
              onChange={(event) => setToken(event.target.value)} disabled={connect.isPending} />
            {error ? <Alert severity="error">{error}</Alert> : null}
            <Button variant="contained" disabled={!token.trim() || connect.isPending}
              onClick={() => connect.mutate(token.trim())} sx={{ alignSelf: 'flex-start' }}>
              {connect.isPending ? <CircularProgress size={20} color="inherit" /> : 'Проверить и подключить'}
            </Button>
          </Stack></CardContent>
        </Card> : null}
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
            {telegram.data?.botConfigured ? <Button variant="outlined" disabled={link.isPending} onClick={() => link.mutate()} sx={{ alignSelf: 'flex-start' }}>Создать ссылку для привязки</Button> : null}
            {telegram.data?.botConfigured && stores.data?.[0] ? <Button variant="outlined" disabled={sendSummary.isPending} onClick={() => sendSummary.mutate(stores.data[0].id)} sx={{ alignSelf: 'flex-start' }}>Отправить вчерашнюю сводку</Button> : null}
            {sendSummary.data ? <Alert severity={sendSummary.data.failed ? 'warning' : 'success'}>Отправлено: {sendSummary.data.sent}; ошибок: {sendSummary.data.failed}.</Alert> : null}
            {link.data?.linkUrl ? <Button href={link.data.linkUrl} target="_blank" rel="noopener noreferrer" sx={{ alignSelf: 'flex-start' }}>Открыть бота и привязать чат</Button> : null}
            {link.data && !link.data.linkUrl ? <Typography>Отправьте боту команду /start {link.data.code}</Typography> : null}
            {link.data ? <Typography variant="caption">Ссылка действует до {new Date(link.data.expiresAtUtc).toLocaleString('ru-RU')}. После привязки обновите страницу.</Typography> : null}
          </Stack>
        </CardContent>
      </Card>
      <Accordion disableGutters elevation={0} sx={{ bgcolor: 'transparent', '&:before': { display: 'none' } }}>
        <AccordionSummary expandIcon="⌄"><Typography variant="body2">Ограничения и подробности</Typography></AccordionSummary>
        <AccordionDetails><Typography variant="body2" color="text.secondary">Загрузка может ждать лимит WB. Ночной запуск, бюджетные уведомления и расписание утренней сводки пока не подключены.</Typography></AccordionDetails>
      </Accordion>
      </Stack></Grid>
      </Grid>
    </Stack>
  );
}
