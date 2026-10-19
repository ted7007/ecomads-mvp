import { Accordion, AccordionDetails, AccordionSummary, Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField, Typography } from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { createTelegramLink, describeSummaryResult, disconnectTelegramChat, getTelegramChats, sendPeriodSummary, sendTelegramTest, sendYesterdaySummary } from './telegramApi';
import type { WbStore } from './wbStoresApi';
import { ConfirmDisconnect, SectionTitle } from './storesUi';
import { summaryPeriodError, moscowTime } from './storesModel';
import { TelegramHistory } from './TelegramHistory';
import { defaultPeriod, moscowToday } from '../DashboardPage/components/PeriodFilter';
import { HttpError } from '../../shared/api/httpClient';
import { lightUiTokens as ui } from '../../app/lightUiTokens';
type SendArgs = { kind: 'yesterday' | 'period' | 'test'; store?: { id: string; name: string }; start: string; end: string; context: string };
export function TelegramCard({ store, storesLoading }: { store?: WbStore; storesLoading: boolean }) {
  const cache = useQueryClient(); const chats = useQuery({ queryKey: ['telegram-chats'], queryFn: getTelegramChats });
  const [showAll, setShowAll] = useState(false), [disconnecting, setDisconnecting] = useState<{ id: string; name: string } | null>(null);
  const [linkedBefore, setLinkedBefore] = useState<string[]>([]), [checked, setChecked] = useState('');
  const [now, setNow] = useState(Date.now());
  const link = useMutation({ mutationFn: createTelegramLink, onMutate: () => { setLinkedBefore(chats.data?.chats.map(c => c.id) ?? []); setChecked(''); } });
  useEffect(() => { if (!link.data) return; const interval = setInterval(() => setNow(Date.now()), 10000); return () => clearInterval(interval); }, [link.data]);
  const expired = !!link.data && Date.parse(link.data.expiresAtUtc) <= now;
  const disconnect = useMutation({ mutationFn: (id: string) => disconnectTelegramChat(id), onSuccess: async () => { setDisconnecting(null); await cache.invalidateQueries({ queryKey: ['telegram-chats'] }); } });
  const [period, setPeriod] = useState(() => defaultPeriod());
  const periodError = summaryPeriodError(period.startDate ?? '', period.endDate ?? '', moscowToday());
  const context = `${store?.id ?? ''}:${period.startDate}:${period.endDate}`;
  const [dismissed, setDismissed] = useState(false);
  const send = useMutation({
mutationFn: async (args: SendArgs): Promise<{ text: string; severity: 'success' | 'info' | 'warning'; label: string }> => {
      if (args.kind === 'test') {
        const result = await sendTelegramTest(); return { text: `Тест: отправлено ${result.sent}, не доставлено ${result.failed}. Данные демонстрационные.`, severity: result.failed ? 'warning' : result.sent ? 'success' : 'info', label: 'Все подключённые чаты' };
      }
      if (!args.store) throw Error('Выберите кабинет WB.');
      if (args.kind === 'yesterday') {
        const result = await sendYesterdaySummary(args.store.id); return { ...describeSummaryResult(result), label: `${args.store.name} · ${result.reportDate}` };
      }
      const result = await sendPeriodSummary(args.store.id, args.start, args.end);
      return { text: `Отправлено ${result.sent}, не доставлено ${result.failed}.${result.partial ? ` Сводка с пропусками по ${result.missingCampaigns} кампаниям.` : ''}`, severity: result.failed || result.partial ? 'warning' : result.sent ? 'success' : 'info', label: `${args.store.name} · ${args.start} — ${args.end}` };
    }, onMutate: () => setDismissed(false), onSuccess: async () => { await Promise.all(['telegram-chats', 'telegram-history'].map(key => cache.invalidateQueries({ queryKey: [key] }))); }
});
  const previousContext = useRef(context);
  useEffect(() => { if (previousContext.current !== context) { previousContext.current = context; if (!send.isPending) send.reset(); } }, [context, send.isPending]);
  const isCurrent = send.variables?.context === context;
  const notReady = isCurrent && send.error instanceof HttpError && send.error.status === 409;
  const ready = chats.data?.botConfigured && !!chats.data.chats.length && !chats.isError;
  const canSend = ready && !!store && !storesLoading && !send.isPending;
  const begin = (kind: SendArgs['kind']) => send.mutate({ kind, store: store ? { id: store.id, name: store.name } : undefined, start: period.startDate ?? '', end: period.endDate ?? '', context });
  return <Card component="section" aria-label="Telegram"><CardContent><Stack spacing={2}>
    <Stack direction="row" alignItems="center" gap={1}><SectionTitle>Telegram</SectionTitle>{chats.data ? <Chip size="small" sx={chats.data.botConfigured && chats.data.chats.length ? ui.status.success : ui.status.neutral} label={!chats.data.botConfigured ? 'Бот не настроен' : chats.data.chats.length ? 'Подключён' : 'Не подключён'} /> : null}</Stack>
    {chats.isPending ? <CircularProgress size={20} aria-label="Загрузка чатов Telegram" /> : null}
    {chats.isError ? <Alert severity="error">Не удалось загрузить чаты. <Button onClick={() => void chats.refetch()}>Повторить</Button></Alert> : null}
    {chats.data && !chats.data.botConfigured ? <Alert severity="info">Бот пока не настроен. Привязка и отправка станут доступны после настройки сервиса.</Alert> : null}
    <Stack spacing={1}><Typography component="h3" variant="subtitle2">Подключённые чаты</Typography>
      {(showAll ? chats.data?.chats : chats.data?.chats.slice(0, 5))?.map(chat => <Stack key={chat.id} direction="row" alignItems="center" gap={1} sx={{ p: 1, bgcolor: ui.surface.soft, borderRadius: 2 }}><Box sx={{ flex: 1, minWidth: 0 }}><Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>{chat.displayName || 'Личный чат'}</Typography><Typography variant="caption" color="text.secondary">С {moscowTime(chat.linkedAtUtc)}</Typography></Box><Button size="small" color="error" disabled={disconnect.isPending || send.isPending} onClick={() => { disconnect.reset(); setDisconnecting({ id: chat.id, name: chat.displayName || 'Личный чат' }); }}>Отключить чат</Button></Stack>)}
      {(chats.data?.chats.length ?? 0) > 5 ? <Button size="small" onClick={() => setShowAll(!showAll)}>{showAll ? 'Свернуть' : `Показать все (${chats.data?.chats.length})`}</Button> : null}
      {chats.data?.botConfigured && !chats.data.chats.length ? <Typography variant="body2" color="text.secondary">Чаты пока не привязаны.</Typography> : null}
      {chats.data?.botConfigured ? <Button variant="outlined" disabled={link.isPending || chats.isError} onClick={() => { setNow(Date.now()); link.mutate(); }} sx={{ alignSelf: 'flex-start' }}>{chats.data.chats.length ? 'Подключить ещё чат' : 'Подключить Telegram'}</Button> : null}
      {link.isError ? <Alert severity="error">Не удалось создать ссылку. Попробуйте ещё раз.</Alert> : null}
      {chats.data?.botConfigured && link.data ? <Box sx={{ p: 1.5, bgcolor: 'action.hover', borderRadius: 2, overflowWrap: 'anywhere' }}>{expired ? <Typography variant="body2">Ссылка истекла. Создайте новую для привязки.</Typography> : <>
        {link.data.linkUrl ? <Button href={link.data.linkUrl} target="_blank" rel="noopener noreferrer">Открыть бота{chats.data?.botUsername ? ` @${chats.data.botUsername}` : ''}</Button> : <Typography variant="body2" sx={{ userSelect: 'text' }}>Отправьте боту команду /start {link.data.code}</Typography>}
        <Typography variant="caption" display="block">Ссылка действует до {moscowTime(link.data.expiresAtUtc)}</Typography>
        <Button size="small" disabled={chats.isFetching} onClick={async () => { const result = await chats.refetch(); if (result.data?.chats.some(c => !linkedBefore.includes(c.id))) { link.reset(); setChecked('Чат подключён.'); } else setChecked(result.isError ? 'Не удалось проверить привязку. Повторите проверку.' : 'Новый чат пока не найден. Откройте бота и нажмите Start.'); }}>Я привязал чат — проверить</Button>
      </>}</Box> : null}
      {checked ? <Typography role="status" variant="caption">{checked}</Typography> : null}
    </Stack>
    <Stack spacing={.5}><Typography component="h3" variant="subtitle2">Время утренней сводки</Typography><Typography variant="body2" color="text.secondary">Автоматическая утренняя сводка пока недоступна.</Typography></Stack>
    <Stack spacing={.5}><Typography component="h3" variant="subtitle2">Уведомления</Typography><Typography variant="body2" color="text.secondary">Настройка уведомлений пока недоступна.</Typography></Stack>
    <Stack spacing={1}><Typography component="h3" variant="subtitle2">Как выглядит сводка</Typography><Box sx={{ p: 1.5, bgcolor: 'rgba(0,122,255,.07)', borderRadius: 3 }}><Box sx={{ p: 1.5, bgcolor: ui.surface.popup, borderRadius: 2 }}><Chip size="small" label="Пример · демонстрационные данные" sx={{ ...ui.status.info, mb: 1, maxWidth: '100%', '& .MuiChip-label': { whiteSpace: 'normal' }, height: 'auto' }} /><Typography variant="body2" fontWeight={700}>EcomAds · Пример кабинета</Typography><Typography variant="caption" color="text.secondary">Рекламная сводка за 18.10.2026, МСК</Typography><Typography variant="body2" sx={{ mt: 1 }}>Расход кабинета: <b>10 000 ₽</b><br />Все заказы, сумма: <b>125 000 ₽</b><br />Общий ДРР: <b>8,0%</b></Typography><Typography variant="body2" sx={{ mt: 1 }}>Расход на рекламу: 8 000 ₽ · Заказы из рекламы: 25 · Рекламная выручка: 80 000 ₽ · Рекламный ДРР: 10,0%</Typography><Typography variant="body2" sx={{ mt: 1 }}>Пледы — 5 000 ₽ · 15 заказов · ДРР 10,0%<br />Полотенца — 3 000 ₽ · 10 заказов · ДРР 10,0%</Typography><Typography variant="caption" color="primary" display="block" sx={{ mt: 1 }}>Открыть сводку за этот день</Typography></Box></Box></Stack>
    <Stack spacing={1} role="region" aria-label="Ручная отправка Telegram"><Typography component="h3" variant="subtitle2">Ручная отправка{store ? ` · ${store.name}` : ''}</Typography>
      {!ready && chats.data?.botConfigured ? <Typography variant="caption" color="text.secondary">Для отправки сначала привяжите чат.</Typography> : null}
      {!store && !storesLoading ? <Typography variant="caption" color="text.secondary">Для сводки подключите кабинет WB.</Typography> : null}
      <Button variant="contained" disabled={!canSend} onClick={() => begin('yesterday')}>Отправить вчерашнюю сводку</Button>
      <Accordion disableGutters elevation={0}><AccordionSummary expandIcon="⌄"><Typography variant="body2">За другой период</Typography></AccordionSummary><AccordionDetails><Stack spacing={1.5}><Stack direction={{ xs: 'column', sm: 'row', lg: 'column' }} gap={1}><TextField type="date" size="small" label="Начало периода" InputLabelProps={{ shrink: true }} value={period.startDate ?? ''} disabled={send.isPending} onChange={e => setPeriod({ ...period, startDate: e.target.value })} /><TextField type="date" size="small" label="Конец периода" InputLabelProps={{ shrink: true }} value={period.endDate ?? ''} disabled={send.isPending} onChange={e => setPeriod({ ...period, endDate: e.target.value })} /></Stack>{periodError ? <Typography variant="caption" color="text.secondary">{periodError}</Typography> : null}<Button variant="outlined" disabled={!canSend || !!periodError} onClick={() => begin('period')}>Отправить данные за период</Button></Stack></AccordionDetails></Accordion>
      <Button size="small" disabled={!ready || send.isPending} onClick={() => begin('test')}>Отправить тестовое сообщение</Button>
      {send.isPending ? <Typography role="status" variant="caption">Отправляем сообщение…</Typography> : null}
      {isCurrent && send.data ? <Alert severity={send.data.severity}>{send.data.label}: {send.data.text}</Alert> : null}
      {isCurrent && send.isError && !notReady ? <Alert severity="error">{send.error instanceof Error ? send.error.message : 'Не удалось отправить сообщение.'}</Alert> : null}
      <Dialog open={!!notReady && !dismissed} onClose={() => setDismissed(true)} fullWidth maxWidth="xs"><DialogTitle>Сводку пока нельзя отправить</DialogTitle><DialogContent><Typography>{send.error?.message}</Typography></DialogContent><DialogActions><Button onClick={() => setDismissed(true)}>Понятно</Button></DialogActions></Dialog>
    </Stack>
    <Typography variant="caption" color="text.secondary">Сводки отправляются в подключённые чаты аккаунта. Предпросмотр выше не отправляет сообщения.</Typography>
    <TelegramHistory />
    <ConfirmDisconnect open={!!disconnecting} title={`Отключить чат «${disconnecting?.name ?? ''}»?`} description="Сводки больше не будут приходить в этот чат. Его можно привязать снова." pending={disconnect.isPending} error={disconnect.isError ? <Alert severity="error">Не удалось отключить чат.</Alert> : undefined} onClose={() => setDisconnecting(null)} onConfirm={() => disconnecting && disconnect.mutate(disconnecting.id)} />
  </Stack></CardContent></Card>;
}
