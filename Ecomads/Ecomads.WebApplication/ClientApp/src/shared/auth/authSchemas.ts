import { z } from 'zod';

export const currentUserSchema = z.object({
  id: z.string(),
  name: z.string(),
  email: z.string().email(),
  phone: z.string().nullable().optional(),
  createdAt: z.string().nullable().optional(),
  lastLoginAt: z.string().nullable().optional(),
  isDemoUser: z.boolean().optional(),
  accessType: z.number().optional(),
  demoStatus: z.number().optional(),
  demoStartedAtUtc: z.string().nullable().optional(),
  demoExpiresAtUtc: z.string().nullable().optional(),
  demoFeedbackSubmittedAtUtc: z.string().nullable().optional(),
  mvpAccessGrantedAtUtc: z.string().nullable().optional()
});

export const tokenResponseSchema = z.object({
  token: z.string().min(1),
  sellerId: z.string(),
  name: z.string(),
  email: z.string().email()
});
