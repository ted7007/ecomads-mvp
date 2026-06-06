import type { AuthTokenResponse, CurrentUser } from './authTypes';

const tokenStorageKey = 'ecomads_token';
const userStorageKey = 'ecomads_user';

export function getToken(): string | null {
  return localStorage.getItem(tokenStorageKey);
}

export function setToken(token: string): void {
  localStorage.setItem(tokenStorageKey, token);
}

export function clearAuth(): void {
  localStorage.removeItem(tokenStorageKey);
  localStorage.removeItem(userStorageKey);
}

export function getCurrentUser(): CurrentUser | null {
  const userJson = localStorage.getItem(userStorageKey);

  if (!userJson) {
    return null;
  }

  try {
    return JSON.parse(userJson) as CurrentUser;
  } catch {
    clearAuth();
    return null;
  }
}

export function setCurrentUser(user: CurrentUser): void {
  localStorage.setItem(userStorageKey, JSON.stringify(user));
}

export function persistAuthResponse(response: AuthTokenResponse): void {
  setToken(response.token);
  setCurrentUser({
    id: response.sellerId,
    name: response.name,
    email: response.email
  });
}

