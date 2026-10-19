import { Alert, Button, Dialog, DialogTitle, DialogContent, DialogActions, Stack, Typography } from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { appRoutes } from '../../app/routes';
import { acceptWbRecalculation, getWbDataState, type WbDataQuality } from '../api/wbDataState';

const names: Record<string, string> = { fullstats: 'Статистика кампаний', expenses: 'Расход кабинета', orders: 'Все заказы',
  clusters: 'Кластеры', jam: 'Джем' };
const jobNames: Record<string, string> = { fullstats: 'текущей рекламы', archive: 'архива рекламы', expenses: 'расхода кабинета',
  funnel_recent: 'свежих заказов', funnel_backfill: 'истории заказов',
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
  const [revision, setRevision] = useState<WbDataQuality | null>(null);
  const accept = useMutation({ mutationFn: acceptWbRecalculation,
    onSuccess: async () => { setRevision(null); await client.invalidateQueries(); } });
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
        source.kind === 'orders' || source.kind === 'expenses' ? ['dashboard-daily', 'wb-coverage'] :
        source.kind === 'clusters' ? ['wb-clusters'] : ['wb-jam'];
      for (const key of keys) void client.invalidateQueries({ queryKey: [key] });
    }
  }, [client, state.data]);
  useEffect(() => { seen.current = {}; }, [startDate, endDate, campaignId]);
  if (state.isError) return <Alert severity="warning">Не удалось проверить свежесть данных WB.</Alert>;
  if (!state.data) return null;
  const qualityIssues = state.data.quality.filter(q => !['data', 'zero', 'accepted'].includes(q.state))
    .sort((a, b) => b.lastAttemptAtUtc.localeCompare(a.lastAttemptAtUtc) || b.startDate.localeCompare(a.startDate));
  return <Stack direction="row" gap={1.5} useFlexGap flexWrap="wrap" sx={{ px: 0.5 }}>
    {state.data.isPreliminary ? <Alert severity="info" sx={{ width: '100%', py: 0 }}>
      Текущий день по Москве ещё не завершён. Показатели предварительные и могут поступать с задержкой WB.
    </Alert> : null}
    {qualityIssues.slice(0, 20).map(q =>
      <Alert severity="warning" key={`${q.storeId}-${q.source}-${q.entity}-${q.startDate}-${q.endDate}`} sx={{ width: '100%' }}>
        {names[q.source] ?? q.source} · {shortDate(q.startDate)} МСК: {q.reason ?? 'Данные требуют проверки.'}
        {' '}Попытка: {moscowTime(q.lastAttemptAtUtc)} МСК.
        {q.acceptedAtUtc ? ` Последнее принятое значение: ${moscowTime(q.acceptedAtUtc)} МСК.` : ''}
        {q.state !== 'revision' && q.previousValue !== null ? ` Сохранённая сумма: ${q.previousValue} ₽; актуальность не подтверждена.` : ''}
        {q.state === 'revision' && q.snapshotId ? <>
          {' '}Прежняя сумма: {q.previousValue} ₽; предлагается: {q.proposedValue} ₽.
          <Button size="small" onClick={() => { accept.reset(); setRevision(q); }}>Принять пересчёт</Button>
        </> : null}
      </Alert>)}
    {qualityIssues.length > 20 ? <Typography variant="caption">Ещё {qualityIssues.length - 20} отметок качества. Выберите более короткий период для просмотра.</Typography> : null}
    <Dialog open={revision !== null} onClose={() => { if (!accept.isPending) setRevision(null); }}>
      <DialogTitle>Принять пересчёт расхода?</DialogTitle>
      <DialogContent>
        Расход за {revision?.startDate} МСК изменится с {revision?.previousValue} ₽ на {revision?.proposedValue} ₽.
        Решение будет записано в журнал.
        {accept.isError ? <Alert severity="error">Не удалось принять пересчёт. Обновите данные и проверьте актуальные суммы.</Alert> : null}
      </DialogContent>
      <DialogActions>
        <Button disabled={accept.isPending} onClick={() => setRevision(null)}>Отмена</Button>
        <Button disabled={accept.isPending || !revision} onClick={() => revision && accept.mutate(revision)}>Принять пересчёт</Button>
      </DialogActions>
    </Dialog>
    {state.data.sources.map((source) => {
      const age = source.lastCheckedAtUtc ? Date.now() - Date.parse(source.lastCheckedAtUtc) : null;
      const progress = source.kind === 'jam' || source.kind === 'clusters' ? '' :
        ` · полностью подтверждено ${source.covered} из ${source.expected} дней`;
      const available = source.availableStartDate && source.availableEndDate ?
        ` · данные ${shortDate(source.availableStartDate)}–${shortDate(source.availableEndDate)}` : '';
      const wait = source.status === 'waiting' && source.waitReason !== 'rollout' && source.nextAttemptAtUtc ?
        ` · следующий запрос ${moscowTime(source.nextAttemptAtUtc)} МСК` : '';
      const status = source.waitReason === 'rollout' ? 'ожидает включения загрузки для кабинета' : source.status === 'loading' ? 'загружается' : source.status === 'waiting' ? 'ожидание лимита WB' :
        source.status === 'failed' ? source.availableStartDate ? 'часть данных сохранена' : 'нет подтверждённых данных' :
        source.status === 'not_loaded' ? 'нет подтверждённых данных' :
        source.status === 'partial' ? 'частично' : 'загружено';
      const failure = source.failedJobKind ?
        ` · последнее обновление ${jobNames[source.failedJobKind ?? ''] ?? 'данных'} не удалось: ${errorNames[source.errorCode ?? ''] ?? 'ошибка загрузки'}` : '';
      const checked = source.lastCheckedAtUtc ? moscowTime(source.lastCheckedAtUtc) : null;
      const estimate = source.waitReason !== 'rollout' && source.estimatedCompletionAtUtc && (source.status === 'waiting' || source.status === 'loading') ?
        ` · оценка завершения ${moscowTime(source.estimatedCompletionAtUtc)} МСК` : '';
      return <Typography key={source.kind} variant="caption" color={source.failedJobKind ? 'error.main' : 'text.secondary'}>
        {names[source.kind]}: {status}{progress}{available}{checked ? ` · проверено ${checked} МСК` : ''}
        {age !== null && age > 86_400_000 ? ' · более суток назад' : ''}{wait}{estimate}{failure}
        {source.noDataCampaigns > 0 ? ` · WB не вернул часть дней по ${source.noDataCampaigns} кампаниям; пропуски не считаются нулями и проверяются при следующем сборе` : ''}
        {source.kind === 'orders' && source.status === 'waiting' ? ' · может потребоваться больше времени' : ''}
      </Typography>;
    })}
    {state.data.sources.some((source) => source.failedJobKind) ?
      <Button size="small" component={Link} to={appRoutes.wbStores} sx={{ py: 0, minWidth: 0 }}>
        Проверить загрузку
      </Button> : null}
  </Stack>;
}
