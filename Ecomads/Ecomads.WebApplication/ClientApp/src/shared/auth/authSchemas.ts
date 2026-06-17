import { z } from 'zod';

export const currentUserSchema = z.object({
  id: z.string(),
  name: z.string(),
  email: z.string().email()
});

export const tokenResponseSchema = z.object({
  token: z.string().min(1),
  sellerId: z.string(),
  name: z.string(),
  email: z.string().email()
});

