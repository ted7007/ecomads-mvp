import { z } from 'zod';
import { httpClient } from '../../shared/api/httpClient';

const chatsSchema = z.object({
  botConfigured: z.boolean(),
  botUsername: z.string().nullable(),
  chats: z.array(z.object({
    id: z.string().uuid(),
    displayName: z.string().nullable(),
    linkedAtUtc: z.string()
  }))
});

const linkSchema = z.object({
  code: z.string(),
  linkUrl: z.string().nullable(),
  expiresAtUtc: z.string()
});

export async function getTelegramChats() {
  return chatsSchema.parse(await httpClient<unknown>('/api/telegram/chats'));
}

export async function createTelegramLink() {
  return linkSchema.parse(await httpClient<unknown>('/api/telegram/link', { method: 'POST' }));
}

export async function disconnectTelegramChat(chatId: string) {
  await httpClient<void>(`/api/telegram/chats/${encodeURIComponent(chatId)}`, { method: 'DELETE' });
}

export async function sendYesterdaySummary(storeId: string) {
  return z.object({ reportDate: z.string(), sent: z.number().int(), skipped: z.number().int(),
    failed: z.number().int() }).parse(await httpClient<unknown>(
      `/api/telegram/stores/${encodeURIComponent(storeId)}/send-yesterday`, { method: 'POST' }));
}
