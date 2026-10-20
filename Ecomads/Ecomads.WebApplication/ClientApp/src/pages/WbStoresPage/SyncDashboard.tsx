import { Accordion, AccordionDetails, AccordionSummary, Alert, Box, Button, Card, CardContent, CircularProgress, Drawer, IconButton, Stack, TextField, MenuItem, Typography } from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useId, useState } from 'react';
import type { ReactNode } from 'react';
import { getWbSyncDetails, getWbSyncHistory, pauseWbSync, resumeWbSync, retryWbSync } from './wbStoresApi';
import type { WbRefreshResult, WbSyncJob, WbSyncOverview } from './wbStoresApi';
import { labels, stages, jobPeriod, jobProgress, jobError, moscowTime, displayJob, primaryKinds, canPause } from './storesModel';
import { JobChip, SectionTitle } from './storesUi';
export { jobStatus, blockingReason, moscowTime } from './storesModel';

function JobDetails({ storeId, job, label, notes = [] }: { storeId: string; job: WbSyncJob; label?: string; notes?: string[] }) {
  const titleId = useId();
  const [open, setOpen] = useState(false);
  const details = useQuery({ queryKey: ['wb-sync-details', storeId, job.id], queryFn: () => getWbSyncDetails(storeId, job.id), enabled: open });
  const cache = useQueryClient();
  const invalidate = () => Promise.all(['wb-sync-overview', 'wb-sync-history', 'wb-sync-details'].map(key => cache.invalidateQueries({ queryKey: [key, storeId] })));
  const retry = useMutation({ mutationFn: () => retryWbSync(storeId, job.id), onSuccess: invalidate });
  const pause = useMutation({ mutationFn: () => job.paused ? resumeWbSync(storeId, job.id) : pauseWbSync(storeId, job.id), onSuccess: invalidate });
  const busy = retry.isPending || pause.isPending;
  return <Stack spacing={.5}>
    <Button size="small" aria-label={`Открыть загрузку: ${label ?? labels[job.kind] ?? job.kind}`} onClick={() => setOpen(true)} sx={label ? { p: 0, justifyContent: 'flex-start', textAlign: 'left', fontWeight: 600, color: 'text.primary' } : undefined}>{label ?? labels[job.kind] ?? job.kind}{label ? ' ›' : ''}</Button>
    {retry.isError || pause.isError ? <Alert severity="error">{(retry.error ?? pause.error)?.message ?? 'Не удалось изменить загрузку.'}</Alert> : null}
    <Drawer anchor="right" open={open} onClose={() => setOpen(false)} PaperProps={{ role: 'dialog', 'aria-labelledby': titleId, sx: { width: { xs: '100%', sm: 520 }, maxWidth: '100%', p: 2.5, bgcolor: 'background.default' } }}>
      <Stack direction="row" alignItems="center" gap={1} sx={{ mb: 2 }}><Typography id={titleId} component="h2" variant="h6" sx={{ flex: 1 }}>{labels[job.kind] ?? job.kind}</Typography><IconButton aria-label="Закрыть подробности загрузки" onClick={() => setOpen(false)}>×</IconButton></Stack><JobChip job={job} />
      <Stack direction="row" useFlexGap flexWrap="wrap" gap={.5} sx={{ my: 1 }}>

        {canPause(job) ? <Button size="small" disabled={busy} onClick={() => pause.mutate()}>{job.paused ? 'Продолжить' : 'Приостановить'}</Button> : null}
        {job.canRetry ? <Button size="small" disabled={busy} onClick={() => retry.mutate()}>Повторить загрузку</Button> : null}
      </Stack>{retry.isError || pause.isError ? <Alert severity="error">{(retry.error ?? pause.error)?.message ?? 'Не удалось изменить загрузку.'}</Alert> : null}
      <Box sx={{ p: 1.5, bgcolor: 'action.hover', borderRadius: 2, overflowWrap: 'anywhere' }}>
        <Typography variant="body2">Период: {jobPeriod(job)} · Обработано {jobProgress(job)}</Typography>
        <Typography variant="body2">Создано {moscowTime(job.createdAtUtc)}</Typography><Typography variant="body2">Завершено {moscowTime(job.completedAtUtc)}</Typography>
        {job.importedRows != null ? <Typography variant="body2">Получено строк: {job.importedRows}</Typography> : null}
        {job.warning ? <Typography variant="body2" color="warning.main">{job.warning}</Typography> : null}
        {job.errorCode ? <Typography variant="body2" color="error.main">{jobError(job.errorCode, job.kind)}</Typography> : null}
        {job.status === 'failed' && job.processedCount > 0 ? <Typography variant="body2">Сохранённые данные доступны.</Typography> : null}
        {!job.paused && ['rate_limit', 'retry'].includes(job.waitReason ?? '') ? <Typography variant="body2">Следующая попытка: {moscowTime(job.nextAttemptAtUtc)}</Typography> : null}
        {job.paused ? <Typography variant="body2">{job.errorCode === 'no_progress' ? 'Загрузка приостановлена: 24 часа без прогресса. Проверьте доступ и лимиты перед продолжением.' : 'Загрузка приостановлена. Уже начатый запрос WB может завершиться.'}</Typography> : null}
        {job.waitReason === 'priority' && !job.paused ? <Typography variant="body2">Продолжится после загрузки текущих данных.</Typography> : null}
        {job.estimatedCompletionAtUtc ? <Typography variant="body2">Завершение примерно: {moscowTime(job.estimatedCompletionAtUtc)}</Typography> : null}
        {details.isPending ? <CircularProgress size={16} aria-label="Загрузка подробностей" /> : null}
        {details.isError ? <Alert severity="error">Не удалось загрузить подробности. <Button onClick={() => void details.refetch()}>Повторить</Button></Alert> : null}
        {details.data?.events.map((event, index) => <Typography variant="caption" display="block" key={`${event.occurredAtUtc}:${index}`}>{moscowTime(event.occurredAtUtc)} · {stages[event.stage] ?? event.stage} · обработано {event.processedCount} · попытка {event.attemptNumber}{event.errorCode ? ` · ${event.errorCode}` : ''}</Typography>)}
        <Typography variant="caption" color="text.secondary">Задание {job.id}</Typography>
        {notes.map((note, index) => <Typography key={index} variant="body2">{note}</Typography>)}
      </Box></Drawer></Stack>;
}
function SourceRow({ source, storeId, skipped }: { source: WbSyncOverview['sources'][number]; storeId: string; skipped: WbRefreshResult['skipped'] }) {
  const job = displayJob(source);
  return <Box role="row" sx={{ py: 1, borderTop: 1, borderColor: 'divider', display: 'grid', gridTemplateColumns: { xs: 'minmax(0,1fr)', sm: 'minmax(0,1fr) 110px minmax(0,1fr)' }, gap: .5, alignItems: 'start' }}>
    <Box role="cell">{job ? <JobDetails key={job.id} storeId={storeId} job={job} label={labels[source.kind] ?? source.kind} notes={[source.blockedReason, skipped.find(s => s.kind === source.kind)?.reason].filter((v): v is string => !!v)} /> : <Typography variant="body2" fontWeight={600}>{labels[source.kind] ?? source.kind}</Typography>}</Box>
    <Typography role="cell" variant="caption" color="text.secondary">{moscowTime(source.lastSuccessAtUtc)}</Typography>
    <Box role="cell" sx={{ minWidth: 0 }}><JobChip job={job} />{!job&&(source.blockedReason||skipped.find(s=>s.kind===source.kind)?.reason)?<Typography variant="caption" display="block" color="text.secondary">{source.blockedReason??skipped.find(s=>s.kind===source.kind)?.reason}</Typography>:null}</Box>
  </Box>;
}
export function SyncDashboard({ storeId, overview, refresh, error, loading = false, controls, skipped }: { storeId: string; overview?: WbSyncOverview; refresh: () => void; error: boolean; loading?: boolean; controls?: ReactNode; skipped: WbRefreshResult['skipped'] }) {
  const [historyOpen, setHistoryOpen] = useState(false), [page, setPage] = useState(1), [kind, setKind] = useState(''), [status, setStatus] = useState('');
  const history = useQuery({ queryKey: ['wb-sync-history', storeId, page, kind, status], queryFn: () => getWbSyncHistory(storeId, page, kind, status), enabled: historyOpen, refetchInterval: historyOpen && overview?.sources.some(s => s.activeJob) ? 15000 : false, refetchIntervalInBackground: false });
  const totalPages = Math.max(1, Math.ceil((history.data?.total ?? 0) / 10));
  useEffect(() => { if (history.data && page > totalPages) setPage(totalPages); }, [history.data, page, totalPages]);
  const main = (overview?.sources ?? []).filter(s => primaryKinds.includes(s.kind)).sort((a, b) => primaryKinds.indexOf(a.kind) - primaryKinds.indexOf(b.kind));
  const extra = (overview?.sources ?? []).filter(s => !primaryKinds.includes(s.kind));
  return <Card component="section" aria-label="Загрузка данных"><CardContent><Stack spacing={1.5}>
    <Stack direction="row" justifyContent="space-between" alignItems="center" gap={1}><SectionTitle>Загрузка данных</SectionTitle><Button size="small" onClick={() => { refresh(); if (historyOpen) void history.refetch(); }}>Обновить статус</Button></Stack>
    {loading ? <CircularProgress size={20} aria-label="Загрузка статусов WB" /> : null}
    {error ? <Alert severity="error">Не удалось обновить статусы. <Button onClick={refresh}>Повторить</Button></Alert> : null}
    {overview ? <Box role="table" aria-label="Источники данных WB"><Box role="row" sx={{ display: { xs: 'none', sm: 'grid' }, gridTemplateColumns: 'minmax(0,1fr) 110px minmax(0,1fr)', gap: .5, pb: 1, color: 'text.secondary', fontSize: 11, textTransform: 'uppercase' }}><span role="columnheader">Источник</span><span role="columnheader">Успешная обработка</span><span role="columnheader">Состояние</span></Box>{main.map(source => <SourceRow key={source.kind} source={source} storeId={storeId} skipped={skipped} />)}</Box> : null}
    {extra.length ? <Accordion disableGutters elevation={0}><AccordionSummary expandIcon="⌄"><Typography variant="body2">История и дополнительные загрузки · активных: {extra.filter(s => s.activeJob).length}</Typography></AccordionSummary><AccordionDetails>{extra.map(source => <SourceRow key={source.kind} source={source} storeId={storeId} skipped={skipped} />)}</AccordionDetails></Accordion> : null}
    {overview && !overview.sources.length ? <Typography variant="body2" color="text.secondary">Загрузки пока не запускались.</Typography> : null}
    {controls}
    <Accordion disableGutters elevation={0}><AccordionSummary expandIcon="⌄"><Typography variant="body2">Как работает загрузка</Typography></AccordionSummary><AccordionDetails><Stack spacing={1}><Typography variant="body2">Постановка текущих задач — с 06:15 и 18:15 МСК. Месячная перепроверка — ежедневно; годовая история дозагружается в свободных слотах. Это время запуска, а не гарантия готовности.</Typography><Typography variant="body2">Ручной сбор выбирает периоды автоматически. Заказы загружаются короткими интервалами с учётом лимитов WB; годовая история может занять несколько дней. Пауза истории не удаляет сохранённые данные.</Typography><Typography variant="body2">Завершение задания означает обработку ответа, а не полное покрытие выбранного периода.</Typography></Stack></AccordionDetails></Accordion>
    <Accordion expanded={historyOpen} onChange={(_, open) => setHistoryOpen(open)} disableGutters elevation={0}><AccordionSummary expandIcon="⌄"><Typography variant="body2">Журнал загрузок</Typography></AccordionSummary><AccordionDetails><Stack spacing={1.5}>
      <Typography variant="caption" color="text.secondary">Сначала новые · по 10 заданий</Typography>
      <Stack direction={{ xs: 'column', sm: 'row' }} gap={1}><TextField select label="Источник загрузки" size="small" value={kind} onChange={e => { setKind(e.target.value); setPage(1); }} sx={{ flex: 1 }}><MenuItem value="">Все источники</MenuItem>{Object.entries(labels).map(([k, v]) => <MenuItem key={k} value={k}>{v}</MenuItem>)}</TextField><TextField select label="Состояние загрузки" size="small" value={status} onChange={e => { setStatus(e.target.value); setPage(1); }} sx={{ flex: 1 }}><MenuItem value="">Все состояния</MenuItem>{Object.entries({ pending: 'В очереди', running: 'Выполняется', completed: 'Завершено', failed: 'Ошибка' }).map(([k, v]) => <MenuItem key={k} value={k}>{v}</MenuItem>)}</TextField></Stack>
      {history.isPending ? <CircularProgress size={20} aria-label="Загрузка журнала WB" /> : null}{history.isError ? <Alert severity="error">Не удалось загрузить журнал. <Button onClick={() => void history.refetch()}>Повторить</Button></Alert> : null}
      {history.data?.items.map(job => <Box key={job.id} sx={{ borderTop: 1, borderColor: 'divider', pt: 1 }}><Stack direction="row" alignItems="center" gap={1} flexWrap="wrap"><JobDetails storeId={storeId} job={job} label={labels[job.kind] ?? job.kind}/><JobChip job={job} /></Stack><Typography variant="caption">{jobPeriod(job)} · {jobProgress(job)}</Typography></Box>)}
      {history.data && !history.data.total ? <Typography color="text.secondary">Загрузок пока не было.</Typography> : null}
      {history.data && history.data.total > 10 ? <Stack direction="row" gap={1} justifyContent="flex-end" alignItems="center"><Button disabled={page <= 1} onClick={() => setPage(page - 1)}>Назад</Button><Typography variant="caption">{page} из {totalPages}</Typography><Button disabled={page >= totalPages} onClick={() => setPage(page + 1)}>Далее</Button></Stack> : null}
    </Stack></AccordionDetails></Accordion>
  </Stack></CardContent></Card>;
}
