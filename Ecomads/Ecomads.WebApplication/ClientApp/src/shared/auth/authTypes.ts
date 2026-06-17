export type CurrentUser = {
  id: string;
  name: string;
  email: string;
  phone?: string | null;
  createdAt?: string | null;
  lastLoginAt?: string | null;
};

export type AuthTokenResponse = {
  token: string;
  sellerId: string;
  name: string;
  email: string;
};

