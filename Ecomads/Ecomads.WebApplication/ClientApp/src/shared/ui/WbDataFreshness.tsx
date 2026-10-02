import { Alert, Button, Stack, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import { Link } from 'react-router-dom';
import { appRoutes } from '../../app/routes';
import { getWbDataState } from '../api/wbDataState';

const names: Record<string, string> = { fullstats: 'Реклама', orders: 'Все заказы',
  clusters: 'Кластеры', jam: 'Джем' };
const jobNames: Record<string, string> = { fullstats: 'текущей рекламы', archive: 'архива рекламы',
  funnel_recent: 'свежих заказов', funnel_backfill: 'истории заказов', funnel: 'заказов',
  clusters: 'кластеров', jam: 'Джема' };
const errorNames: Record<string, string> = { invalid_response: 'не удалось обработать ответ WB',
  transport_error: 'нет связи с WB', token_missing: 'токен отсутствует',
  token_unreadable: 'токен недоступен' };
const shortDate = (date: string) => date.replace(/^(\d{4})-(\d{2})-(\d{2})$/, '$3.$2');
const moscowTime = (value: string) => new Intl.DateTimeFormat('ru-RU', { timeZone: 'Europe/Moscow',
  day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }).format(new Date(value));

export function WbDataFreshness({ startDate, endDate, campaignId }: {
  startDate: string; endDate: string; campaignId?: string;
}) {
  const client = useQueryClient();
  const seen = useRef<Record<string, string>>({});
  const state = useQuery({ queryKey: ['wb-data-state', startDate, endDate, campaignId],
    queryFn: () => getWbDataState(startDate, endDate, campaignId),
    refetchInterval: (query) => query.state.data?.active ? 15_000 : 60_000,
    refetchIntervalInBackground: false, refetchOnWindowFocus: true });
  useEffect(() => {
    for (const source of state.data?.sources ?? []) {
      const before = seen.current[source.kind];
      seen.current[source.kind] = source.version;
      if (!before || before === source.version) continue;
      const keys = source.kind === 'fullstats' ? ['projects', 'dashboard-daily', 'campaign-daily',
        'campaign-summary', 'campaign-nomenclatures', 'wb-spend-trend', 'wb-coverage', 'loaded-periods'] :
        source.kind === 'orders' ? ['dashboard-daily', 'wb-coverage'] :
        source.kind === 'clusters' ? ['wb-clusters'] : ['wb-jam'];
      for (const key of keys) void client.invalidateQueries({ queryKey: [key] });
    }
  }, [client, state.data]);
  useEffect(() => { seen.current = {}; }, [startDate, endDate, campaignId]);
  if (state.isError) return <Alert severity="warning">Не удалось проверить свежесть данных WB.</Alert>;
  if (!state.data) return null;
  return <Stack direction="row" gap={1.5} useFlexGap flexWrap="wrap" sx={{ px: 0.5 }}>
    {state.data.sources.map((source) => {
      const age = source.lastCheckedAtUtc ? Date.now() - Date.parse(source.lastCheckedAtUtc) : null;
      const progress = source.kind === 'jam' || source.kind === 'clusters' ? '' :
        ` · полностью подтверждено ${source.covered} из ${source.expected} дней`;
      const available = source.availableStartDate && source.availableEndDate ?
        ` · данные ${shortDate(source.availableStartDate)}–${shortDate(source.availableEndDate)}` : '';
      const wait = source.status === 'waiting' && source.nextAttemptAtUtc ?
        ` · следующий запрос ${moscowTime(source.nextAttemptAtUtc)} МСК` : '';
      const status = source.status === 'loading' ? 'загружается' : source.status === 'waiting' ? 'ожидание лимита WB' :
        source.status === 'failed' ? source.availableStartDate ? 'часть данных сохранена' : 'нет подтверждённых данных' :
        source.status === 'not_loaded' ? 'нет подтверждённых данных' :
        source.status === 'partial' ? 'частично' : 'загружено';
      const failure = source.status === 'failed' ?
        ` · последнее обновление ${jobNames[source.failedJobKind ?? ''] ?? 'данных'} не удалось: ${errorNames[source.errorCode ?? ''] ?? 'ошибка загрузки'}` : '';
      const checked = source.lastCheckedAtUtc ? moscowTime(source.lastCheckedAtUtc) : null;
      const estimate = source.estimatedCompletionAtUtc && (source.status === 'waiting' || source.status === 'loading') ?
        ` · оценка завершения ${moscowTime(source.estimatedCompletionAtUtc)} МСК` : '';
      return <Typography key={source.kind} variant="caption" color={source.status === 'failed' ? 'error.main' : 'text.secondary'}>
        {names[source.kind]}: {status}{progress}{available}{checked ? ` · проверено ${checked} МСК` : ''}
        {age !== null && age > 86_400_000 ? ' · более суток назад' : ''}{wait}{estimate}{failure}
        {source.kind === 'orders' && source.status === 'waiting' ? ' · может потребоваться больше времени' : ''}
      </Typography>;
    })}
    {state.data.sources.some((source) => source.status === 'failed') ?
      <Button size="small" component={Link} to={appRoutes.wbStores} sx={{ py: 0, minWidth: 0 }}>
        Проверить загрузку
      </Button> : null}
  </Stack>;
}
