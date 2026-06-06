export type CurrentUser = {
  id: string;
  name: string;
  email: string;
};

export type AuthTokenResponse = {
  token: string;
  sellerId: string;
  name: string;
  email: string;
};

