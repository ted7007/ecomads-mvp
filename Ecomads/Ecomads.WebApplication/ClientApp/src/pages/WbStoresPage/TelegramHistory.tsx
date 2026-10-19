import { Accordion, AccordionDetails, AccordionSummary, Alert, Box, Button, Chip, CircularProgress, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { getTelegramHistory, describeDeliveryStatus } from './telegramApi';
import { historyRows, sortedHistory, moscowTime } from './storesModel';
import { PaginationBar } from '../../shared/ui/PaginationBar';
import { lightUiTokens as ui } from '../../app/lightUiTokens';
export function TelegramHistory() {
  const [open, setOpen] = useState(false), [page, setPage] = useState(0), [size, setSize] = useState(5), [sort, setSort] = useState<'lastAtUtc' | 'reportDate' | 'storeName'>('lastAtUtc'), [asc, setAsc] = useState(false);
  const query = useQuery({ queryKey: ['telegram-history'], queryFn: getTelegramHistory, enabled: open });
  const rows = sortedHistory(historyRows(query.data ?? []), sort, asc);
  const safePage = Math.min(page, Math.max(0, Math.ceil(rows.length / size) - 1));
  return <Accordion expanded={open} onChange={(_, v) => setOpen(v)} disableGutters elevation={0}><AccordionSummary expandIcon="⌄"><Typography component="h3" variant="subtitle2">Последние отправки за день</Typography></AccordionSummary><AccordionDetails><Stack spacing={1.5}>
    <Typography variant="caption" color="text.secondary">До 30 последних событий. Отправки за произвольный период сюда не записываются.</Typography>
    <Stack direction="row" gap={.5}><TextField select size="small" label="Сортировка отправок" value={sort} sx={{ flex: 1 }} onChange={e => { setSort(e.target.value as typeof sort); setAsc(e.target.value === 'storeName'); setPage(0); }}><MenuItem value="lastAtUtc">Время изменения</MenuItem><MenuItem value="reportDate">Дата отчёта</MenuItem><MenuItem value="storeName">Кабинет</MenuItem></TextField><Button size="small" aria-label={asc ? 'Сортировать отправки по убыванию' : 'Сортировать отправки по возрастанию'} onClick={() => { setAsc(!asc); setPage(0); }}>{asc ? '↑' : '↓'}</Button></Stack>
    {query.isPending ? <CircularProgress size={20} aria-label="Загрузка истории Telegram" /> : null}
    {query.isError ? <Alert severity="error">Не удалось загрузить историю Telegram. <Button onClick={() => void query.refetch()}>Повторить</Button></Alert> : null}
    {!query.isPending && !query.isError && !rows.length ? <Typography color="text.secondary">Сводки за день ещё не отправлялись.</Typography> : null}
    {rows.slice(safePage * size, (safePage + 1) * size).map(event => <Box key={event.key} sx={{ borderTop: 1, borderColor: 'divider', pt: 1 }}><Typography variant="body2" fontWeight={700}>Сводка за {event.reportDate.split('-').reverse().join('.')} · {event.storeName}</Typography><Typography variant="caption" color="text.secondary">Обновлено {moscowTime(event.lastAtUtc)}</Typography><Stack spacing={.5} sx={{ mt: .5 }}>{event.chats.map((chat, index) => <Stack key={index} direction="row" alignItems="center" gap={.5} flexWrap="wrap"><Typography variant="body2">{chat.name || 'Личный чат'}</Typography><Chip size="small" sx={chat.status === 'sent' ? ui.status.success : chat.status === 'pending' ? ui.status.info : ui.status.warning} label={describeDeliveryStatus(chat.status)} />{chat.errorCode ? <Typography variant="caption" color="text.secondary">{chat.errorCode}</Typography> : null}</Stack>)}</Stack></Box>)}
    {rows.length ? <PaginationBar desktopSizeButtons count={rows.length} page={safePage} rowsPerPage={size} onPageChange={setPage} onRowsPerPageChange={n => { setSize(n); setPage(0); }} /> : null}
  </Stack></AccordionDetails></Accordion>;
}
