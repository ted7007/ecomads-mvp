export type CurrentUser = {
  id: string;
  name: string;
  email: string;
  phone?: string | null;
  createdAt?: string | null;
  lastLoginAt?: string | null;
  isDemoUser?: boolean;
  accessType?: number;
  demoStatus?: number;
  demoStartedAtUtc?: string | null;
  demoExpiresAtUtc?: string | null;
  demoFeedbackSubmittedAtUtc?: string | null;
  mvpAccessGrantedAtUtc?: string | null;
};

export type AuthTokenResponse = {
  token: string;
  sellerId: string;
  name: string;
  email: string;
};

