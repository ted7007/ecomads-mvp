export type Attribution = {
  utm_source: string;
  captured_at_utc: string;
};

const storageKey = 'ecomads.firstAttribution';
const lifetimeMs = 30 * 24 * 60 * 60 * 1000;
let memoryAttribution: Attribution | undefined;

function validAttribution(value: unknown, now: number): value is Attribution {
  if (!value || typeof value !== 'object') return false;
  const { utm_source, captured_at_utc } = value as Partial<Attribution>;
  if (typeof utm_source !== 'string' || utm_source.length < 1 || utm_source.length > 128 ||
      /[^A-Za-z0-9._-]/.test(utm_source) || typeof captured_at_utc !== 'string') return false;
  const capturedAt = Date.parse(captured_at_utc);
  return Number.isFinite(capturedAt) && new Date(capturedAt).toISOString() === captured_at_utc &&
    capturedAt <= now && capturedAt > now - lifetimeMs;
}

export function getAttribution(now = Date.now()): Attribution | undefined {
  try {
    const stored: unknown = JSON.parse(localStorage.getItem(storageKey) ?? 'null');
    if (validAttribution(stored, now)) {
      memoryAttribution = { utm_source: stored.utm_source, captured_at_utc: stored.captured_at_utc };
      return { ...memoryAttribution };
    }
  } catch {
    // Registration also works with unavailable storage or damaged JSON.
  }
  if (validAttribution(memoryAttribution, now)) return { ...memoryAttribution };
  memoryAttribution = undefined;
  return undefined;
}

export function captureAttribution(url: string, now = Date.now()): void {
  if (getAttribution(now)) return;
  const sources = new URL(url).searchParams.getAll('utm_source');
  if (sources.length !== 1) return;
  const candidate = { utm_source: sources[0], captured_at_utc: new Date(now).toISOString() };
  if (!validAttribution(candidate, now)) return;
  memoryAttribution = candidate;
  try {
    localStorage.setItem(storageKey, JSON.stringify(candidate));
  } catch {
    // Keep the first source for this page when persistence is unavailable.
  }
}
