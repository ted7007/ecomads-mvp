import { useQuery } from '@tanstack/react-query';
import { z } from 'zod';
import { httpClient } from '../../shared/api/httpClient';

const registrationDocumentsSchema = z.object({
  version: z.string().regex(/^[a-f0-9]{64}$/),
  privacy: z.string().min(1),
  consent: z.string().min(1)
});

export function useRegistrationDocuments() {
  return useQuery({
    queryKey: ['registration-documents'],
    queryFn: async ({ signal }) => registrationDocumentsSchema.parse(
      await httpClient<unknown>('/api/legal/registration', { skipAuth: true, signal })
    ),
    retry: false,
    staleTime: Infinity
  });
}
