import { Accordion, AccordionDetails, AccordionSummary, Alert, Box, Button, Chip, Collapse, MenuItem, Select, Stack, Typography } from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { getWbSyncDetails, getWbSyncHistory, retryWbSync } from './wbStoresApi';
import type { WbRefreshResult, WbSyncJob, WbSyncOverview } from './wbStoresApi';

const labels: Record<string, string> = { fullstats: 'Статистика кампаний', funnel: 'Все заказы (воронка продаж)',
  clusters: 'Поисковые кластеры', jam: 'Поисковые запросы Джема' };
const units: Record<string, [string, string, string]> = {
  campaign: ['кампания', 'кампании', 'кампаний'],
  pair: ['пара «кампания/артикул»', 'пары «кампания/артикул»', 'пар «кампания/артикул»'],
  product: ['товар', 'товара', 'товаров'],
  request: ['запрос', 'запроса', 'запросов']
};
const stages: Record<string, string> = { queued: 'В очереди', waiting: 'Ожидание', requesting: 'Запрос к WB', importing: 'Сохранение данных', completed: 'Завершено', failed: 'Ошибка' };

export function moscowTime(value?: string | null): string {
  if (!value) return '—';
  return `${new Intl.DateTimeFormat('ru-RU', { timeZone: 'Europe/Moscow', day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' }).format(new Date(value))} МСК`;
}

function shortDate(value: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  return match ? `${match[3]}.${match[2]}.${match[1]}` : value;
}

function jobPeriod(job: WbSyncJob): string {
  if (job.kind === 'funnel' && job.totalCount > 1) {
    const end = new Date(`${job.endDate}T00:00:00Z`);
    end.setUTCDate(end.getUTCDate() - 29);
    return `${shortDate(end.toISOString().slice(0, 10))}–${shortDate(job.endDate)}`;
  }
  return `${shortDate(job.startDate)}–${shortDate(job.endDate)}`;
}

function jobProgress(job: WbSyncJob): string {
  const count = new Intl.NumberFormat('ru-RU').format(job.processedCount);
  const total = job.totalCount > 0 ? ` из ${new Intl.NumberFormat('ru-RU').format(job.totalCount)}` : '';
  const quantity = job.totalCount > 0 ? job.totalCount : job.processedCount;
  const forms = units[job.unit];
  const word = quantity % 100 >= 11 && quantity % 100 <= 14 ? forms[2] :
    quantity % 10 === 1 ? forms[0] : quantity % 10 >= 2 && quantity % 10 <= 4 ? forms[1] : forms[2];
  return `${count}${total} ${word}`;
}

export function jobStatus(job: WbSyncJob | null): string {
  if (!job) return 'Ещё не запускалось';
  if (job.status === 'completed') return 'Завершено';
  if (job.status === 'failed') return 'Ошибка';
  if (job.stage === 'requesting') return 'Запрос к WB';
  if (job.stage === 'importing') return 'Сохраняем данные';
  if (job.waitReason === 'retry') return 'Ожидает повторной попытки';
  if (job.waitReason === 'rate_limit') {
    return Date.now() > Date.parse(job.nextAttemptAtUtc) + 120000 ? 'Запуск задерживается' : 'Ожидание лимита WB';
  }
  return 'В очереди';
}

export function jobError(code: string | null, kind?: string): string {
  if (!code) return '';
  if (code === 'wb_401' || code === 'token_missing' || code === 'token_unreadable' || code === 'token_disconnected') return 'Проверьте подключение токена WB.';
  if (code === 'wb_403') return kind === 'funnel' ? 'В токене нет доступа к категории «Аналитика».' :
    'WB отказал в доступе. Проверьте права токена и доступ к отчёту.';
  if (code === 'wb_402') return 'WB запросил оплату доступа к отчёту.';
  if (code === 'wb_429') return 'Достигнут лимит WB. Система повторит запрос после ожидания.';
  if (code === 'invalid_response') return 'WB вернул данные в неожиданном формате. Требуется проверка интеграции.';
  if (code === 'transport_error' || code.startsWith('wb_5')) return 'Временный сбой WB или связи. Система повторит запрос.';
  if (code === 'internal_error') return 'Ошибка обработки данных. Повторите загрузку после проверки подробностей.';
  return 'Загрузка не удалась. Откройте подробности.';
}

export function blockingReason(overview?: WbSyncOverview): string | null {
  const job = overview?.activeJob;
  if (!job) return null;
  if (overview?.blockedReason) return overview.blockedReason;
  const wait = job.waitReason === 'rate_limit' || job.waitReason === 'retry';
  return `${labels[job.kind]}: ${jobStatus(job).toLowerCase()}.${wait ? ` Следующая попытка ${moscowTime(job.nextAttemptAtUtc)}.` : ' Дождитесь завершения.'}`;
}

function JobDetails({ storeId, job }: { storeId: string; job: WbSyncJob }) {
  const [open, setOpen] = useState(false);
  const details = useQuery({ queryKey: ['wb-sync-details', storeId, job.id],
    queryFn: () => getWbSyncDetails(storeId, job.id), enabled: open });
  const queryClient = useQueryClient();
  const retry = useMutation({ mutationFn: () => retryWbSync(storeId, job.id), onSuccess: async () => {
    await Promise.all([queryClient.invalidateQueries({ queryKey: ['wb-sync-overview', storeId] }),
      queryClient.invalidateQueries({ queryKey: ['wb-sync-history', storeId] })]);
  } });
  return <>
    <Button size="small" onClick={() => setOpen(!open)}>{open ? 'Скрыть' : 'Подробности'}</Button>
    {job.canRetry ? <Button size="small" disabled={retry.isPending} onClick={() => retry.mutate()}>Повторить</Button> : null}
    <Collapse in={open}><Box sx={{ p: 1.5, bgcolor: 'rgba(0,122,255,.04)', borderRadius: '8px' }}>
      <Typography variant="body2">Задание {job.id} · создано {moscowTime(job.createdAtUtc)}</Typography>
      {job.kind === 'fullstats' ? <Typography variant="body2">Кампаний: {details.data?.campaignIds.length ?? job.totalCount}</Typography> : null}
      {details.data?.events.map((event, index) => <Typography key={`${event.occurredAtUtc}-${index}`} variant="body2" color="text.secondary">
        {moscowTime(event.occurredAtUtc)} · {stages[event.stage] || event.stage} · {event.processedCount} обработано{event.attemptNumber ? ` · попытка ${event.attemptNumber}` : ''}
        {event.errorCode ? ` · ${event.errorCode}` : ''}
      </Typography>)}
      {details.isError ? <Alert severity="error">Не удалось загрузить подробности задания.</Alert> : null}
      {retry.isError ? <Alert severity="error">{retry.error instanceof Error ? retry.error.message : 'Не удалось повторить загрузку'}</Alert> : null}
    </Box></Collapse>
  </>;
}

export function SyncDashboard({ storeId, overview, refresh, error, skipped }: {
  storeId: string; overview?: WbSyncOverview; refresh: () => void; error: boolean;
  skipped: WbRefreshResult['skipped'];
}) {
  const [page, setPage] = useState(1);
  const [kind, setKind] = useState('');
  const [status, setStatus] = useState('');
  const history = useQuery({ queryKey: ['wb-sync-history', storeId, page, kind, status],
    queryFn: () => getWbSyncHistory(storeId, page, kind, status), refetchInterval: overview?.activeJob ? 15000 : false,
    refetchIntervalInBackground: false, refetchOnWindowFocus: true });
  const [checkedAt, setCheckedAt] = useState<Date | null>(null);
  useEffect(() => { if (overview) setCheckedAt(new Date()); }, [overview]);
  return <Stack spacing={1.5}>
    <Stack direction="row" justifyContent="space-between" alignItems="center" flexWrap="wrap" gap={1}>
      <Typography variant="h6" fontWeight={800}>Загрузка данных</Typography>
      <Button size="small" variant="outlined" onClick={() => { refresh(); void history.refetch(); }}>Обновить статус</Button>
    </Stack>
    {error ? <Alert severity="warning">Не удалось обновить статус. Последнее обновление: {checkedAt ? moscowTime(checkedAt.toISOString()) : 'неизвестно'}.</Alert> : null}
    {overview?.sources.map((source) => <Box key={source.kind} sx={{ py: 1, borderTop: '1px solid', borderColor: 'divider' }}>
      <Stack direction="row" justifyContent="space-between" alignItems="center" gap={1}>
        <Typography fontWeight={700}>{labels[source.kind]}</Typography>
        <Chip size="small" label={jobStatus(source.activeJob ?? source.lastJob)} color={source.lastJob?.status === 'failed' ? 'error' : source.lastJob?.status === 'completed' ? 'success' : 'default'} />
      </Stack>
      <Typography variant="body2" color="text.secondary" sx={{ mt: 0.4 }}>Последняя успешная загрузка: {moscowTime(source.lastSuccessAtUtc)}</Typography>
      {source.lastJob ? <>
        <Typography variant="body2" sx={{ mt: 0.4 }}>Период {jobPeriod(source.lastJob)} · обработано {jobProgress(source.lastJob)}</Typography>
        {source.lastJob.status === 'failed' && source.lastJob.processedCount > 0 ? <Typography variant="body2">Сохранённые данные доступны.</Typography> : null}
        {source.lastJob.errorCode ? <Typography variant="body2" color="error.main">{jobError(source.lastJob.errorCode, source.kind)}</Typography> : null}
        {source.activeJob?.waitReason === 'rate_limit' ? <Typography variant="body2">
          Следующий запрос в {moscowTime(source.activeJob.nextAttemptAtUtc)} — лимит WB.
        </Typography> : null}
      </> : null}
      {skipped.find((item) => item.kind === source.kind)?.reason ? <Typography variant="body2" color="text.secondary">
        Пропущено: {skipped.find((item) => item.kind === source.kind)?.reason}
      </Typography> : null}
      <Stack direction="row" alignItems="center" gap={1} flexWrap="wrap" sx={{ mt: 0.5 }}>
        {source.lastJob ? <JobDetails storeId={storeId} job={source.lastJob} /> : null}
      </Stack>
    </Box>)}
    <Accordion disableGutters elevation={0} sx={{ bgcolor: 'transparent', '&:before': { display: 'none' } }}>
      <AccordionSummary expandIcon="⌄"><Typography fontWeight={700}>Журнал загрузок</Typography></AccordionSummary>
      <AccordionDetails><Stack spacing={1.5}>
    <Stack direction="row" gap={1} flexWrap="wrap">
      <Select size="small" value={kind} onChange={(e) => { setKind(e.target.value); setPage(1); }} displayEmpty aria-label="Тип загрузки">
        <MenuItem value="">Все источники</MenuItem>{Object.entries(labels).map(([key, label]) => <MenuItem value={key} key={key}>{label}</MenuItem>)}
      </Select>
      <Select size="small" value={status} onChange={(e) => { setStatus(e.target.value); setPage(1); }} displayEmpty aria-label="Состояние загрузки">
        <MenuItem value="">Все состояния</MenuItem>{['pending', 'running', 'completed', 'failed'].map((key) => <MenuItem value={key} key={key}>{({ pending: 'В очереди', running: 'Выполняется', completed: 'Завершено', failed: 'Ошибка' } as Record<string,string>)[key]}</MenuItem>)}
      </Select>
    </Stack>
    {history.isError ? <Alert severity="error">Не удалось загрузить историю.</Alert> : null}
    {history.data?.items.length ? <Box sx={{ borderTop: '1px solid', borderColor: 'divider' }}>
      <Box sx={{ display: { xs: 'none', md: 'grid' }, gridTemplateColumns: '1.3fr 1fr .8fr 92px', gap: 1, py: 1,
        color: 'text.secondary', fontSize: 11, fontWeight: 700, textTransform: 'uppercase' }}>
        <span>Источник</span><span>Период</span><span>Состояние</span><span />
      </Box>
      {history.data.items.map((job) => <Box key={job.id} sx={{ borderTop: '1px solid', borderColor: 'divider', py: 1 }}>
        <Box sx={{ display: { xs: 'block', md: 'grid' }, gridTemplateColumns: '1.3fr 1fr .8fr 92px', gap: 1,
          alignItems: 'center', '& > *': { minWidth: 0 } }}>
          <Typography variant="body2" fontWeight={700}>{labels[job.kind]}</Typography>
          <Typography variant="body2">{jobPeriod(job)}</Typography>
          <Typography variant="body2" color={job.status === 'failed' ? 'error.main' : 'text.secondary'}>{jobStatus(job)}</Typography>
          <Box><JobDetails storeId={storeId} job={job} /></Box>
        </Box>
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
          Обработано {jobProgress(job)} · создано {moscowTime(job.createdAtUtc)} · завершено {moscowTime(job.completedAtUtc)}
        </Typography>
      </Box>)}
    </Box> : null}
    {history.data && !history.data.total ? <Typography color="text.secondary">Загрузок пока не было.</Typography> : null}
    {history.data && history.data.total > 10 ? <Stack direction="row" alignItems="center" gap={1} justifyContent="flex-end">
      <Button disabled={page === 1} onClick={() => setPage(page - 1)}>Назад</Button>
      <Typography>{page} из {Math.ceil(history.data.total / 10)}</Typography>
      <Button disabled={page * 10 >= history.data.total} onClick={() => setPage(page + 1)}>Вперёд</Button>
    </Stack> : null}
      </Stack></AccordionDetails>
    </Accordion>
  </Stack>;
}
