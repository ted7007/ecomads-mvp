import { z } from 'zod';
import { httpClient } from '../api/httpClient';
import { currentUserSchema, tokenResponseSchema } from './authSchemas';
import type { AuthTokenResponse, CurrentUser } from './authTypes';

export type LoginRequest = {
  email: string;
  password: string;
};

export type RegisterRequest = {
  name: string;
  email: string;
  password: string;
};

export async function login(request: LoginRequest): Promise<AuthTokenResponse> {
  const response = await httpClient<unknown>('/api/auth/login', {
    method: 'POST',
    body: request,
    skipAuth: true
  });

  return tokenResponseSchema.parse(response);
}

export async function register(request: RegisterRequest): Promise<AuthTokenResponse> {
  const response = await httpClient<unknown>('/api/auth/register', {
    method: 'POST',
    body: request,
    skipAuth: true
  });

  return tokenResponseSchema.parse(response);
}

export async function getCurrentUserFromApi(): Promise<CurrentUser> {
  const response = await httpClient<unknown>('/api/auth/me');
  return currentUserSchema.parse(response);
}

export function parseAuthTokenResponse(value: unknown): AuthTokenResponse {
  return tokenResponseSchema.parse(value);
}

export function parseCurrentUser(value: unknown): CurrentUser {
  return currentUserSchema.parse(value);
}

export { z };

