import { Alert, Box, Button, Card, CardContent, CircularProgress, Stack, TextField, Typography } from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { connectWbStore, disconnectWbStore, getWbStores, getWbSync, startWbClusterSync, startWbJamSync, startWbSync } from './wbStoresApi';
import type { WbStore } from './wbStoresApi';
import { createTelegramLink, disconnectTelegramChat, getTelegramChats, sendYesterdaySummary } from './telegramApi';

function StoreCard({ store }: { store: WbStore }) {
  const [error, setError] = useState<string | null>(null);
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [campaignIds, setCampaignIds] = useState('');
  const queryClient = useQueryClient();
  const sync = useQuery({
    queryKey: ['wb-sync', store.id],
    queryFn: () => getWbSync(store.id),
    refetchInterval: (query) => query.state.data?.status === 'running' || query.state.data?.status === 'pending' ? 15000 : false
  });
  useEffect(() => {
    if (sync.data?.status === 'completed') {
      void queryClient.invalidateQueries({ queryKey: ['wb-stores'] });
      void queryClient.invalidateQueries({ queryKey: ['projects'] });
    }
  }, [queryClient, sync.data?.id, sync.data?.status]);
  const start = useMutation({
    mutationFn: () => startWbSync(store.id, {
      startDate: startDate || undefined,
      endDate: endDate || undefined,
      campaignIds: campaignIds.trim() ? campaignIds.split(',').map((id) => Number(id.trim())) : undefined
    }),
    onSuccess: async () => {
      setError(null);
      await queryClient.invalidateQueries({ queryKey: ['wb-sync', store.id] });
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
      await queryClient.invalidateQueries({ queryKey: ['wb-sync', store.id] });
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
      await queryClient.invalidateQueries({ queryKey: ['wb-sync', store.id] });
    },
    onError: (reason) => setError(reason instanceof Error ? reason.message : 'Не удалось запустить отчёт Джема')
  });
  const disconnect = useMutation({
    mutationFn: () => disconnectWbStore(store.id),
    onSuccess: async () => queryClient.invalidateQueries({ queryKey: ['wb-stores'] }),
    onError: (reason) => setError(reason instanceof Error ? reason.message : 'Не удалось отключить кабинет WB')
  });
  const sendSummary = useMutation({
    mutationFn: () => sendYesterdaySummary(store.id),
    onSuccess: () => setError(null),
    onError: (reason) => setError(reason instanceof Error ? reason.message : 'Не удалось отправить сводку')
  });
  const active = sync.data?.status === 'pending' || sync.data?.status === 'running';

  return (
    <Card>
      <CardContent>
        <Stack spacing={1}>
          <Typography variant="h6">{store.name}</Typography>
          <Typography variant="body2">Токен: ••••{store.tokenLastFour} · действует до {new Date(store.tokenExpiresAtUtc).toLocaleDateString('ru-RU')}</Typography>
          <Typography variant="body2">Кампаний: {store.campaignCount}</Typography>
          <Typography variant="body2">Данные: {store.lastSyncAt ? `обновлены ${new Date(store.lastSyncAt).toLocaleString('ru-RU')}` : 'ещё не загружены'}</Typography>
          <Typography variant="body2">Джем: {store.jamStatus === 'active' ? 'отчёт доступен' :
            store.jamStatus === 'access_denied' ? 'WB отказал в доступе' :
            store.jamStatus === 'payment_required' ? 'WB запросил оплату доступа' : 'ещё не проверен'}
            {store.jamCheckedAtUtc ? ` · проверен ${new Date(store.jamCheckedAtUtc).toLocaleString('ru-RU')}` : ''}</Typography>
          {sync.data ? <Typography variant="body2">
            Сбор {sync.data.kind === 'clusters' ? 'кластеров' : sync.data.kind === 'jam' ? 'поисковых запросов Джема' : 'статистики кампаний'} {sync.data.status === 'completed' ? 'завершён' : sync.data.status === 'failed' ? 'не удался' : sync.data.status === 'running' ? 'выполняется или ждёт лимит WB' : 'в очереди'}:
            {' '}{sync.data.processedCampaigns} из {sync.data.totalCampaigns} {sync.data.kind === 'clusters' ? 'пар кампания/артикул' : sync.data.kind === 'jam' ? 'товаров' : 'кампаний'}.
            {active ? ` Следующая попытка: ${new Date(sync.data.nextAttemptAtUtc).toLocaleString('ru-RU')}.` : ''}
            {sync.data.errorCode ? ` Код ошибки: ${sync.data.errorCode}.` : ''}
          </Typography> : null}
          {error ? <Alert severity="error">{error}</Alert> : null}
          <Typography variant="body2" color="text.secondary">
            Статистику кампаний загрузим за последние 30 завершённых дней, кластеры и поисковые запросы Джема — за 7 дней после сбора статистики кампаний.
            Для быстрой сверки укажите период и ID нужных кампаний, например 35174765, 35736322.
          </Typography>
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
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ pt: 1 }}>
            <Button variant="contained" disabled={active || start.isPending || Boolean(startDate) !== Boolean(endDate)
              || Boolean(campaignIds.trim()) && !/^\d+(\s*,\s*\d+)*$/.test(campaignIds.trim())} onClick={() => start.mutate()}>
              {active ? 'Сбор идёт' : 'Загрузить кампании'}
            </Button>
            <Button variant="outlined" disabled={active || startClusters.isPending || Boolean(startDate) !== Boolean(endDate)
              || Boolean(campaignIds.trim()) && !/^\d+(\s*,\s*\d+)*$/.test(campaignIds.trim())} onClick={() => startClusters.mutate()}>
              Загрузить кластеры
            </Button>
            <Button variant="outlined" disabled={active || startJam.isPending || Boolean(startDate) !== Boolean(endDate)
              || Boolean(campaignIds.trim()) && !/^\d+(\s*,\s*\d+)*$/.test(campaignIds.trim())} onClick={() => startJam.mutate()}>
              Загрузить Джем
            </Button>
            <Button color="error" variant="outlined" disabled={disconnect.isPending} onClick={() => disconnect.mutate()}>
              Отключить токен
            </Button>
            <Button variant="outlined" disabled={sendSummary.isPending} onClick={() => sendSummary.mutate()}>
              {sendSummary.isPending ? 'Отправляем…' : 'Отправить вчерашнюю сводку в Telegram'}
            </Button>
          </Stack>
          {sendSummary.data ? <Alert severity={sendSummary.data.failed ? 'warning' : 'success'}>
            Отправлено: {sendSummary.data.sent}; уже обработано: {sendSummary.data.skipped}; ошибок: {sendSummary.data.failed}.
          </Alert> : null}
        </Stack>
      </CardContent>
    </Card>
  );
}

export function WbStoresPage() {
  const [token, setToken] = useState('');
  const [error, setError] = useState<string | null>(null);
  const queryClient = useQueryClient();
  const stores = useQuery({ queryKey: ['wb-stores'], queryFn: getWbStores });
  const telegram = useQuery({ queryKey: ['telegram-chats'], queryFn: getTelegramChats });
  const link = useMutation({ mutationFn: createTelegramLink, onSuccess: () => setError(null),
    onError: (reason) => setError(reason instanceof Error ? reason.message : 'Не удалось создать ссылку') });
  const disconnectChat = useMutation({ mutationFn: disconnectTelegramChat,
    onSuccess: async () => queryClient.invalidateQueries({ queryKey: ['telegram-chats'] }),
    onError: (reason) => setError(reason instanceof Error ? reason.message : 'Не удалось отключить чат') });
  const connect = useMutation({
    mutationFn: connectWbStore,
    onSuccess: async () => {
      setToken('');
      setError(null);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['wb-stores'] }),
        queryClient.invalidateQueries({ queryKey: ['projects'] })
      ]);
    },
    onError: (reason) => setError(reason instanceof Error ? reason.message : 'Не удалось подключить кабинет WB')
  });

  return (
    <Stack spacing={3} sx={{ maxWidth: 850 }}>
      <Box>
        <Typography variant="h4" fontWeight={700}>Кабинеты WB</Typography>
        <Typography color="text.secondary">Подключите кабинет базовым токеном только для чтения.</Typography>
      </Box>

      {stores.isError ? <Alert severity="error">Не удалось загрузить список кабинетов.</Alert> : null}
      {stores.data?.map((store) => <StoreCard key={store.id} store={store} />)}

      <Card>
        <CardContent>
          <Stack spacing={2}>
            <Typography variant="h6">Telegram</Typography>
            {telegram.isError ? <Alert severity="error">Не удалось загрузить привязанные чаты.</Alert> : null}
            {telegram.data && !telegram.data.botConfigured ? <Alert severity="info">Бот ещё не настроен. Привязка и отправка сводки станут доступны после добавления токена бота.</Alert> : null}
            {telegram.data?.chats.map((chat) => <Stack key={chat.id} direction={{ xs: 'column', sm: 'row' }} spacing={1} alignItems={{ sm: 'center' }}>
              <Typography sx={{ flex: 1 }}>{chat.displayName || 'Личный чат'} · с {new Date(chat.linkedAtUtc).toLocaleDateString('ru-RU')}</Typography>
              <Button color="error" size="small" disabled={disconnectChat.isPending} onClick={() => disconnectChat.mutate(chat.id)}>Отключить</Button>
            </Stack>)}
            {telegram.data?.chats.length === 0 && telegram.data.botConfigured ? <Typography color="text.secondary">Чаты пока не привязаны.</Typography> : null}
            <Button variant="outlined" disabled={!telegram.data?.botConfigured || link.isPending} onClick={() => link.mutate()} sx={{ alignSelf: 'flex-start' }}>Создать ссылку для привязки</Button>
            {link.data?.linkUrl ? <Button href={link.data.linkUrl} target="_blank" rel="noopener noreferrer" sx={{ alignSelf: 'flex-start' }}>Открыть бота и привязать чат</Button> : null}
            {link.data && !link.data.linkUrl ? <Typography>Отправьте боту команду /start {link.data.code}</Typography> : null}
            {link.data ? <Typography variant="caption">Ссылка действует до {new Date(link.data.expiresAtUtc).toLocaleString('ru-RU')}. После привязки обновите страницу.</Typography> : null}
          </Stack>
        </CardContent>
      </Card>

      <Card>
        <CardContent>
          <Stack spacing={2}>
            <Typography variant="h6">{stores.data?.length ? 'Добавить или заменить токен' : 'Подключить кабинет'}</Typography>
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
      </Card>
    </Stack>
  );
}
