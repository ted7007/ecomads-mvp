import { appRoutes } from '../../app/routes';

export function safeReturnTo(value: string | null | undefined): string {
  if (!value || !value.startsWith('/') || value.startsWith('//') || /[\\\u0000-\u0020]/.test(value)) return appRoutes.dashboard;
  try {
    const url = new URL(value, 'https://ecomads.ru');
    if (url.origin !== 'https://ecomads.ru' ||
      !/^\/(dashboard|wb-stores|norms|results|demo-feedback|campaign\/[^/]+|admin\/pilot-applications)\/?$/.test(url.pathname))
      return appRoutes.dashboard;
    return `${url.pathname}${url.search}${url.hash}`;
  } catch { return appRoutes.dashboard; }
}

export function withReturnTo(path: string, value: string): string {
  return `${path}?returnTo=${encodeURIComponent(safeReturnTo(value))}`;
}
