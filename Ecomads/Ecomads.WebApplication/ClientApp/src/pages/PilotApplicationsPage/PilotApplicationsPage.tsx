import { useState } from 'react';
import { Alert, Box, Button, Card, CardContent, MenuItem, Pagination, Stack, TextField, Typography } from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { z } from 'zod';
import { httpClient } from '../../shared/api/httpClient';
import { useSession } from '../../shared/auth/useSession';

const statuses = { new: 'Новая', contacted: 'Связались', 'in-pilot': 'В пилоте', closed: 'Закрыта', spam: 'Спам' };
const itemSchema = z.object({ id: z.string().uuid(), name: z.string(), contact: z.string(), stores: z.string().nullable(),
  budget: z.string().nullable(), jam: z.string().nullable(), status: z.enum(['new', 'contacted', 'in-pilot', 'closed', 'spam']),
  note: z.string(), createdAtUtc: z.string(), updatedAtUtc: z.string() });
const listSchema = z.object({ items: z.array(itemSchema), total: z.number(), page: z.number(), pageSize: z.number() });
type Item = z.infer<typeof itemSchema>;
const labels: Record<string, string> = { one: 'Один', 'two-five': '2–5', 'over-five': 'Больше 5', agency: 'Агентство',
  'under-100k': 'До 100 тыс. ₽', '100k-500k': '100–500 тыс. ₽', '500k-1m': '500 тыс. – 1 млн ₽', 'over-1m': 'Больше 1 млн ₽',
  yes: 'Есть', no: 'Нет', unknown: 'Не знаю' };

export function PilotApplicationsPage() {
  const session = useSession();
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState('');
  const query = useQuery({ queryKey: ['pilot-applications', page, status], enabled: session.data?.canManagePilotApplications === true,
    queryFn: async () => listSchema.parse(await httpClient(`/api/admin/pilot-applications?page=${page}${status ? `&status=${status}` : ''}`)), retry: false });
  if (!session.data?.canManagePilotApplications) return <Alert severity="warning">Доступ к заявкам разрешён только команде EcomAds.</Alert>;
  return <Stack spacing={3}>
    <Typography component="h1" variant="h4" fontWeight={800}>Заявки в пилот</Typography>
    <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap">
      <TextField select label="Статус" value={status} sx={{ minWidth: 200 }} onChange={e => { setStatus(e.target.value); setPage(1); }}>
        <MenuItem value="">Все</MenuItem>{Object.entries(statuses).map(([value, label]) => <MenuItem value={value} key={value}>{label}</MenuItem>)}
      </TextField>
      <Typography color="text.secondary">Всего: {query.data?.total ?? '…'}</Typography>
      <Button onClick={() => void query.refetch()}>Обновить</Button>
    </Stack>
    {query.isPending && <Typography>Загружаем заявки…</Typography>}
    {query.isError && <Alert severity="error">Не удалось загрузить заявки. Повторите запрос.</Alert>}
    {query.data?.items.length === 0 && <Typography>Заявок пока нет.</Typography>}
    {query.data?.items.map(item => <ApplicationCard key={`${item.id}:${item.updatedAtUtc}`} item={item} />)}
    {query.data && query.data.total > 20 && <Pagination page={page} count={Math.ceil(query.data.total / 20)} onChange={(_, p) => setPage(p)} />}
    <Typography variant="body2" color="text.secondary">Контакты и заявки автоматически удаляются через 180 дней с создания.</Typography>
  </Stack>;
}

function ApplicationCard({ item }: { item: Item }) {
  const [status, setStatus] = useState(item.status);
  const [note, setNote] = useState(item.note);
  const client = useQueryClient();
  const mutation = useMutation({ mutationFn: () => httpClient(`/api/admin/pilot-applications/${item.id}`, { method: 'PATCH', body: { status, note } }),
    onSuccess: () => client.invalidateQueries({ queryKey: ['pilot-applications'] }) });
  return <Card><CardContent><Stack spacing={2}>
    <Box><Typography component="h2" variant="h6">{item.name}</Typography>
      <Typography sx={{ overflowWrap: 'anywhere' }}>{item.contact}</Typography>
      <Typography variant="body2" color="text.secondary">{new Date(item.createdAtUtc).toLocaleString('ru-RU')} · Кабинетов: {labels[item.stores ?? ''] ?? 'Не указано'} · Бюджет: {labels[item.budget ?? ''] ?? 'Не указано'} · Джем: {labels[item.jam ?? ''] ?? 'Не указано'}</Typography></Box>
    <TextField select label="Статус заявки" value={status} disabled={mutation.isPending} onChange={e => setStatus(e.target.value as Item['status'])}>
      {Object.entries(statuses).map(([value, label]) => <MenuItem key={value} value={value}>{label}</MenuItem>)}
    </TextField>
    <TextField label="Заметка команды" multiline minRows={2} value={note} disabled={mutation.isPending} inputProps={{ maxLength: 2000 }} onChange={e => setNote(e.target.value)} />
    {mutation.isError && <Alert severity="error">Не удалось сохранить изменения.</Alert>}
    {mutation.isSuccess && <Alert severity="success">Сохранено</Alert>}
    <Button variant="contained" disabled={mutation.isPending || (status === item.status && note === item.note)} onClick={() => mutation.mutate()} sx={{ alignSelf: 'flex-start' }}>Сохранить</Button>
  </Stack></CardContent></Card>;
}
