import type { WbSyncJob, WbSyncOverview } from './wbStoresApi';
import type { getTelegramHistory } from './telegramApi';
import { sortRows } from '../../shared/lib/sortRows';
export const labels: Record<string, string> = {
  funnel: 'Заказы', fullstats_month: 'Реклама · проверка месяца',
  expenses_month: 'Расходы · проверка месяца', funnel_month: 'Заказы · проверка месяца', fullstats: 'Текущая реклама', product_orders: 'Заказы по товарам · 30 дней',
  fullstats_history: 'История рекламы · год', expenses_history: 'История расходов · год',
  funnel_year: 'История заказов · год',
  funnel_recent: 'Свежие заказы · 7 дней', funnel_backfill: 'История заказов · дни 8–30', expenses: 'Расход кабинета', archive: 'Архив рекламы',
  clusters: 'Поисковые кластеры', jam: 'Поисковые запросы Джема'
};
const units: Record<string, [string, string, string]> = {
  campaign: ['кампания', 'кампании', 'кампаний'],
  pair: ['пара «кампания/артикул»', 'пары «кампания/артикул»', 'пар «кампания/артикул»'],
  product: ['товар', 'товара', 'товаров'],
  request: ['запрос', 'запроса', 'запросов']
};
export const stages: Record<string, string> = { queued: 'В очереди', waiting: 'Ожидание', requesting: 'Запрос к WB', importing: 'Сохранение данных', completed: 'Завершено', failed: 'Ошибка', paused: 'Пауза', resumed: 'Продолжено' };
export const yearKinds = new Set(['fullstats_history', 'expenses_history', 'funnel_year']);

export function moscowTime(value?: string | null): string {
  if (!value) return '—';
  return `${new Intl.DateTimeFormat('ru-RU', { timeZone: 'Europe/Moscow', day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' }).format(new Date(value))} МСК`;
}

function shortDate(value: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  return match ? `${match[3]}.${match[2]}.${match[1]}` : value;
}

export function jobPeriod(job: WbSyncJob): string {
  if (job.kind === 'funnel' && job.totalCount > 1) {
    const end = new Date(`${job.endDate}T00:00:00Z`);
    end.setUTCDate(end.getUTCDate() - 29);
    return `${shortDate(end.toISOString().slice(0, 10))}–${shortDate(job.endDate)}`;
  }
  return `${shortDate(job.startDate)}–${shortDate(job.endDate)}`;
}

export function jobProgress(job: WbSyncJob): string {
  if ((job.kind === 'fullstats' || job.kind === 'fullstats_month' || job.kind === 'archive') && job.retryCount && job.totalCount >= job.retryCount) {
    const initialCount = job.totalCount - job.retryCount;
    const checked = Math.min(job.processedCount, initialCount);
    const repeated = Math.max(0, job.processedCount - initialCount);
    return `${checked} из ${initialCount} кампаний · повторно ${repeated} из ${job.retryCount}`;
  }
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
  if (job.status === 'completed') return 'Обработка завершена';
  if (job.status === 'failed') return 'Ошибка';
  if (job.paused) return 'Приостановлено';
  if (job.stage === 'requesting') return 'Запрос к WB';
  if (job.stage === 'importing') return 'Сохраняем данные';
  if (job.waitReason === 'retry') return 'Ожидает повторной попытки';
  if (job.waitReason === 'priority') return 'Ожидание текущих данных';
  if (job.waitReason === 'rollout') return 'Ожидание включения загрузки для кабинета';
  if (job.waitReason === 'rate_limit') {
    return Date.now() > Date.parse(job.nextAttemptAtUtc) + 120000 ? 'Запуск задерживается' : 'Ожидание лимита WB';
  }
  return 'В очереди';
}

export function jobError(code: string | null, kind?: string): string {
  if (!code) return '';
  if (code === 'wb_401' || code === 'token_missing' || code === 'token_unreadable' || code === 'token_disconnected') return 'Проверьте подключение токена WB.';
  if (code === 'wb_403') return kind?.startsWith('funnel') ? 'В токене нет доступа к категории «Аналитика».' :
    'WB отказал в доступе. Проверьте права токена и доступ к отчёту.';
  if (code === 'wb_402') return 'WB запросил оплату доступа к отчёту.';
  if (code === 'wb_429') return 'Достигнут лимит WB. Система повторит запрос после ожидания.';
  if (code === 'invalid_response' && kind === 'expenses_history') return 'WB прислал списание с датой вне нужного периода. Система повторит запрос с перекрытием дат.';
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


export const displayJob = (source: WbSyncOverview['sources'][number]) => source.activeJob ?? source.lastJob;
export function jobTone(job: WbSyncJob | null): 'success' | 'error' | 'info' | 'warning' | 'neutral' {
  if (!job) return 'neutral';
  if (job.status === 'completed') return 'success';
  if (job.status === 'failed') return 'error';
  if (job.paused) return 'neutral';
  if (job.waitReason === 'rate_limit' || job.waitReason === 'retry') return 'warning';
  if (job.waitReason === 'priority' || job.waitReason === 'rollout') return 'neutral';
  return job.status === 'running' ? 'info' : 'neutral';
}
export const canPause = (job: WbSyncJob) => (yearKinds.has(job.kind) || job.kind === 'product_orders') && ['pending', 'running'].includes(job.status);
export const primaryKinds = ['fullstats', 'expenses', 'funnel', 'funnel_recent', 'funnel_backfill', 'product_orders', 'clusters', 'jam'];
export function summaryPeriodError(start: string, end: string, today: string): string | null {
  const valid = (date: string) => /^\d{4}-\d{2}-\d{2}$/.test(date) && Number.isFinite(Date.parse(date)) && new Date(date).toISOString().slice(0, 10) === date;
  if (!valid(start) || !valid(end) || start > end) return 'Выберите корректные даты периода.';
  if (end >= today) return 'Можно отправить только завершённые дни: последняя дата — вчера.';
  if ((Date.parse(end) - Date.parse(start)) / 86400000 + 1 > 31) return 'Период не длиннее 31 дня.';
  return null;
}
export function historyRows(events: Awaited<ReturnType<typeof getTelegramHistory>>) {
  const duplicates = new Map<string, number>();
  return events.map(event => {
    const fingerprint = JSON.stringify([event.storeName, event.reportDate, event.lastAtUtc, event.chats]);
    const index = duplicates.get(fingerprint) ?? 0;
    duplicates.set(fingerprint, index + 1);
    return { ...event, key: fingerprint + ':' + index };
  });
}
export function sortedHistory(events: ReturnType<typeof historyRows>, key: 'reportDate' | 'lastAtUtc' | 'storeName', ascending: boolean) {
  return sortRows(events, row => key === 'storeName' ? row.storeName : Date.parse(row[key]), ascending, row => row.key);
}
