import { Button, Card, CardContent, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { appRoutes } from '../../app/routes';
import { getWbDataState } from '../api/wbDataState';
import { getWbStores } from '../../pages/WbStoresPage/wbStoresApi';

const dateTime = (value: string) => new Intl.DateTimeFormat('ru-RU', { timeZone: 'Europe/Moscow', day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }).format(new Date(value));

export function DashboardDataStatus({ startDate, endDate, sidebar = false }: { startDate: string; endDate: string; sidebar?: boolean }) {
  const stores = useQuery({ queryKey: ['wb-stores'], queryFn: getWbStores });
  const state = useQuery({ queryKey: ['wb-data-state', startDate, endDate, undefined], queryFn: () => getWbDataState(startDate, endDate),
    refetchInterval: query => query.state.data?.active ? 15_000 : 60_000, refetchIntervalInBackground: false });
  const sources = state.data?.sources ?? [];
  const pending = sources.filter(source => source.status !== 'complete');
  const failed = pending.some(source => source.status === 'failed');
  const nextAttempt = pending.map(source => source.nextAttemptAtUtc).filter((date): date is string => !!date && Date.parse(date) > Date.now()).sort()[0];
  const knownStores = stores.data ?? [];
  if (stores.isSuccess && knownStores.length === 0) return null;
  const allSynced = knownStores.length > 0 && knownStores.every(store => store.lastSyncAt && Number.isFinite(Date.parse(store.lastSyncAt)));
  const syncedAt = allSynced ? knownStores.map(store => store.lastSyncAt!).sort()[0] : null;
  const updated = syncedAt ? `Реклама обновлена ${dateTime(syncedAt)} МСК${knownStores.length > 1 ? ' · самый ранний сбор по кабинетам' : ''}` : 'Время успешной загрузки рекламы пока неизвестно';
  const content = <Stack spacing={1}>
    <Typography variant="caption" fontWeight={600} sx={sidebar ? undefined : { display: { xs: 'block', md: 'none' } }}>{updated}</Typography>
    {sidebar ? <>
      <Typography variant="caption" color={failed ? 'error.main' : 'text.secondary'}>
        {state.isError ? 'Не удалось проверить состояние данных' : state.isPending ? 'Проверяем данные…' : state.data?.active ? 'Загрузка данных идёт' : pending.length ? 'Есть незагруженные или неполные данные' : sources.length ? 'Данные выбранного периода загружены' : 'Состояние загрузки пока неизвестно'}
      </Typography>
      {nextAttempt ? <Typography variant="caption" color="text.secondary">Повторная попытка: {dateTime(nextAttempt)} МСК</Typography> : null}
      <Button component={Link} to={appRoutes.wbStores} variant="outlined" size="small">Управление загрузкой</Button>
    </> : null}
  </Stack>;
  return sidebar ? <Card sx={{ flexShrink: 0, my: 1 }}><CardContent sx={{ p: 1.5, '&:last-child': { pb: 1.5 } }}>{content}</CardContent></Card> :
    <Typography variant="caption" color="text.secondary" sx={{ display: { xs: 'block', md: 'none' } }}>{updated}</Typography>;
}
