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
    failed: z.number().int(), unknown: z.number().int().default(0), partial: z.boolean().default(false), missingCampaigns: z.number().int().default(0) }).parse(await httpClient<unknown>(
      `/api/telegram/stores/${encodeURIComponent(storeId)}/send-yesterday`, { method: 'POST' }));
}

export async function sendTelegramTest() {
  return z.object({ sent: z.number().int(), failed: z.number().int() }).parse(
    await httpClient<unknown>('/api/telegram/send-test', { method: 'POST' }));
}

export type SummaryResult = Awaited<ReturnType<typeof sendYesterdaySummary>>;

export function describeSummaryResult(result: SummaryResult): { severity: 'success' | 'info' | 'warning'; text: string } {
  const parts: string[] = [];
  if (result.sent) parts.push(`отправлено: ${result.sent}`);
  if (result.skipped) parts.push('сводка за этот день уже отправлена, повтор не нужен');
  if (result.failed) parts.push(`не доставлено: ${result.failed} (можно нажать кнопку ещё раз)`);
  if (result.unknown) parts.push(`исход неизвестен: ${result.unknown} (проверьте чат, автоматически не повторяем, чтобы не было дубля)`);
  if (result.partial) parts.push(`сводка с пропусками: WB не вернул данные по ${result.missingCampaigns} кампаниям`);
  const severity = result.failed || result.unknown || result.partial ? 'warning' : result.sent ? 'success' : 'info';
  return { severity, text: parts.join('; ') + '.' };
}

export async function sendPeriodSummary(storeId: string, startDate: string, endDate: string) {
  const query = new URLSearchParams({ startDate, endDate }).toString();
  return z.object({ sent: z.number().int(), failed: z.number().int(), partial: z.boolean().default(false),
    missingCampaigns: z.number().int().default(0) }).parse(await httpClient<unknown>(
    `/api/telegram/stores/${encodeURIComponent(storeId)}/send-period?${query}`, { method: 'POST' }));
}

const historySchema = z.array(z.object({
  storeName: z.string(),
  reportDate: z.string(),
  lastAtUtc: z.string(),
  chats: z.array(z.object({ name: z.string().nullable(), status: z.string(), errorCode: z.string().nullable() }))
}));

export async function getTelegramHistory() {
  return historySchema.parse(await httpClient<unknown>('/api/telegram/history'));
}

export function describeDeliveryStatus(status: string): string {
  switch (status) {
    case 'sent': return 'доставлено';
    case 'failed': return 'не доставлено';
    case 'unknown': return 'исход неизвестен';
    case 'pending': return 'отправляется';
    default: return status;
  }
}
