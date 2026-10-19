import { Accordion, AccordionDetails, AccordionSummary, Alert, Button, Card, CardContent, CircularProgress, Stack, TextField, Typography } from '@mui/material';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { connectWbStore } from './wbStoresApi';
import { invalidateStoreData } from './WbStorePanel';
import { SectionTitle } from './storesUi';
export function TokenConnectionCard({ hasStores, replacing, onCancel, onConnected }: { hasStores: boolean; replacing?: string; onCancel: () => void; onConnected: (id: string) => void }) {
  const [token, setToken] = useState('');
  const input = useRef<HTMLInputElement>(null); const cache = useQueryClient();
  useEffect(() => { setToken(''); if (replacing) input.current?.focus(); }, [replacing]);
  const connect = useMutation({ mutationFn: () => connectWbStore(token.trim()), onSuccess: async store => { setToken(''); onConnected(store.id); await invalidateStoreData(cache); } });
  return <Card component="section" aria-label="Подключение токена"><CardContent><Stack spacing={1.5}>
    <SectionTitle>{replacing ? `Заменить токен · ${replacing}` : hasStores ? 'Подключить ещё кабинет' : 'Подключить кабинет WB'}</SectionTitle>
    <Typography component="ol" variant="body2" sx={{ m: 0, pl: 2.5, '& li': { mb: .5 } }}><li>В ЛК WB откройте интеграции по API и создайте отдельный токен для EcomAds.</li><li>Выберите тип «Базовый».</li><li>Категории: «Продвижение», «Аналитика», «Статистика». Доступ — «Только чтение», тестовый контур выключен.</li><li>Вставьте токен ниже и проверьте подключение.</li></Typography>
    {replacing ? <Typography variant="caption" color="text.secondary">Тот же ID продавца обновит токен с сохранением истории. Другой ID добавит новый кабинет.</Typography> : null}
    <TextField inputRef={input} label="Токен WB" type="password" autoComplete="off" fullWidth value={token} onChange={e => setToken(e.target.value)} disabled={connect.isPending} />
    {connect.isError ? <Alert severity="error">{connect.error instanceof Error ? connect.error.message : 'Не удалось подключить кабинет.'}</Alert> : null}
    {connect.isSuccess ? <Typography role="status" color="success.main" variant="body2">Кабинет подключён.</Typography> : null}
    <Stack direction="row" gap={1}><Button variant="contained" disabled={!token.trim() || connect.isPending} onClick={() => connect.mutate()}>{connect.isPending ? <CircularProgress size={18} color="inherit" aria-label="Проверка токена" /> : 'Проверить и подключить'}</Button>{replacing || token ? <Button disabled={connect.isPending} onClick={() => { setToken(''); connect.reset(); onCancel(); }}>Отмена</Button> : null}</Stack>
    <Accordion disableGutters elevation={0}><AccordionSummary expandIcon="⌄"><Typography variant="caption">О категориях и безопасности</Typography></AccordionSummary><AccordionDetails><Typography variant="body2">«Продвижение» и «Только чтение» обязательны. «Аналитика» нужна для всех заказов и Джема. Окончательную проверку выполняет сервер. Используйте отдельный базовый токен; другие токены того же кабинета делят лимиты WB.</Typography></AccordionDetails></Accordion>
  </Stack></CardContent></Card>;
}
