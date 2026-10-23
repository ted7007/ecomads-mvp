import { useQuery } from '@tanstack/react-query';
import { Alert, Button, CircularProgress, Stack } from '@mui/material';
import { HttpError, httpClient } from '../api/httpClient';
import { currentUserSchema } from './authSchemas';
import { getToken } from './tokenStorage';

export function useSession() {
  const token = getToken();
  const query = useQuery({
    queryKey: ['auth-session', token], enabled: Boolean(token), retry: false, staleTime: 60_000,
    queryFn: async () => currentUserSchema.parse(await httpClient<unknown>('/api/auth/me', {
      skipAuth: true, headers: { Authorization: `Bearer ${token}` }
    }))
  });
  const invalid = query.error instanceof HttpError && [401, 404].includes(query.error.status);
  return { ...query, token, invalid };
}

export function SessionStatus({ error, retry }: { error?: boolean; retry: () => void }) {
  return <Stack alignItems="center" justifyContent="center" spacing={2} sx={{ minHeight: '100dvh', p: 3 }}>
    {error ? <><Alert severity="warning">Не удалось проверить вход. Проверьте соединение и попробуйте ещё раз.</Alert>
      <Button onClick={retry} variant="contained">Повторить проверку</Button></> :
      <><CircularProgress aria-label="Проверяем вход" /><span>Проверяем вход…</span></>}
  </Stack>;
}
