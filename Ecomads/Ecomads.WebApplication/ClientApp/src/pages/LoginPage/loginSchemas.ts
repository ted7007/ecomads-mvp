import { z } from 'zod';

export const loginFormSchema = z.object({
  email: z.string().trim().min(1, 'Введите email').email('Введите корректный email'),
  password: z.string().min(1, 'Введите пароль')
});

export const registerFormSchema = z.object({
  stores: z.enum(['', 'one', 'two-five', 'over-five', 'agency']),
  budget: z.enum(['', 'under-100k', '100k-500k', '500k-1m', 'over-1m']),
  jam: z.enum(['', 'yes', 'no', 'unknown']),
  name: z.string().trim().min(1, 'Введите имя'),
  email: z.string().trim().min(1, 'Введите email').email('Введите корректный email'),
  password: z.string().min(6, 'Пароль должен быть не короче 6 символов')
});

export type LoginFormValues = z.infer<typeof loginFormSchema>;
export type RegisterFormValues = z.infer<typeof registerFormSchema>;

