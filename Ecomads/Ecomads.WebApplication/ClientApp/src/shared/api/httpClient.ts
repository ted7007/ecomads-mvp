import { clearAuth, getToken } from '../auth/tokenStorage';

export type HttpMethod = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';

export type HttpClientOptions = Omit<RequestInit, 'body' | 'method'> & {
  method?: HttpMethod;
  body?: BodyInit | unknown;
  skipAuth?: boolean;
};

export class HttpError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly response: Response
  ) {
    super(message);
    this.name = 'HttpError';
  }
}

export async function httpClient<TResponse>(url: string, options: HttpClientOptions = {}): Promise<TResponse> {
  const response = await sendRequest(url, options);

  if (response.status === 204) {
    return undefined as TResponse;
  }

  const contentType = response.headers.get('content-type') ?? '';

  if (!contentType.includes('application/json')) {
    return (await response.text()) as TResponse;
  }

  return (await response.json()) as TResponse;
}

export async function sendRequest(url: string, options: HttpClientOptions = {}): Promise<Response> {
  const { body, headers, skipAuth = false, ...requestOptions } = options;
  const requestHeaders = new Headers(headers);
  const token = getToken();

  if (!skipAuth && token) {
    requestHeaders.set('Authorization', `Bearer ${token}`);
  }

  const isFormData = typeof FormData !== 'undefined' && body instanceof FormData;
  const requestBody = isFormData || typeof body === 'string' || body instanceof Blob ? body : JSON.stringify(body);

  if (body !== undefined && !isFormData && !requestHeaders.has('Content-Type')) {
    requestHeaders.set('Content-Type', 'application/json');
  }

  const response = await fetch(url, {
    ...requestOptions,
    body: requestBody as BodyInit | undefined,
    headers: requestHeaders
  });

  if (response.status === 401) {
    clearAuth();
    window.location.href = '/login';
  }

  if (response.status === 403) {
    const redirectTo = await getRedirectTo(response.clone());
    if (redirectTo && window.location.pathname !== redirectTo) {
      window.location.href = redirectTo;
    }
  }

  if (!response.ok) {
    throw new HttpError(await getErrorMessage(response), response.status, response);
  }

  return response;
}

async function getRedirectTo(response: Response): Promise<string | null> {
  const contentType = response.headers.get('content-type') ?? '';
  if (!contentType.includes('application/json')) {
    return null;
  }

  const data = (await response.json().catch(() => null)) as { redirectTo?: string } | null;
  return typeof data?.redirectTo === 'string' ? data.redirectTo : null;
}

async function getErrorMessage(response: Response): Promise<string> {
  const contentType = response.headers.get('content-type') ?? '';

  if (contentType.includes('application/json')) {
    const data = (await response.json().catch(() => null)) as { message?: string } | null;
    return data?.message ?? `HTTP error ${response.status}`;
  }

  return (await response.text().catch(() => '')) || `HTTP error ${response.status}`;
}

