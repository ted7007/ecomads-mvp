import { z } from 'zod';
import { httpClient } from '../../shared/api/httpClient';

export const featureDescriptions = {
  dashboard: {
    name: 'Обзор рекламы',
    description: 'Общая картина по рекламе: расходы, заказы, ДРР и ключевые показатели.'
  },
  wb_connection: {
    name: 'Кабинет WB',
    description: 'Подключение кабинета и ручное обновление данных через WB API.'
  },
  clusters: {
    name: 'Поисковые кластеры',
    description: 'Расходы, клики и заказы по кластерам WB.'
  },
  norms: {
    name: 'Нормы',
    description: 'Целевой ДРР и пороги анализа кампаний.'
  },
  telegram: {
    name: 'Telegram',
    description: 'Привязка чата и ручная сводка.'
  },
  feedback_form: {
    name: 'Обратная связь',
    description: 'Форма обратной связи после демо-доступа.'
  }
} as const;

export const primaryTaskOptions = [
  { value: 'reduce_drr', label: 'Снизить ДРР / расходы на рекламу' },
  { value: 'find_waste', label: 'Найти неэффективные расходы' },
  { value: 'configure_norms', label: 'Настроить цели и нормы' },
  { value: 'understand_campaign_stats', label: 'Быстро разобраться в статистике по кампании' },
  { value: 'other', label: 'Другое' }
] as const;

export const usedSectionOptions = [
  { value: 'dashboard', label: 'Сводка' },
  { value: 'campaign_summary', label: 'Сводка по кампании' },
  { value: 'clusters', label: 'Поисковые кластеры' },
  { value: 'norms', label: 'Нормы' },
  { value: 'telegram', label: 'Telegram' }
] as const;

export const mostUsefulFeatureOptions = [
  ...usedSectionOptions,
  { value: 'nothing_useful', label: 'Ничего не было полезно' }
] as const;

export const missingForDecisionOptions = [
  { value: 'clearer_metrics', label: 'Более понятные показатели' },
  { value: 'longer_history', label: 'Более длинная история' },
  { value: 'recommendations', label: 'Конкретные рекомендации' },
  { value: 'telegram_updates', label: 'Больше возможностей Telegram' },
  { value: 'wb_action_instruction', label: 'Понятная инструкция, что сделать в кабинете WB' },
  { value: 'nothing_missing', label: 'Ничего, всё было достаточно понятно' },
  { value: 'other', label: 'Другое' }
] as const;

export const clarityScoreOptions = [
  { value: 1, label: '1 — совсем непонятно' },
  { value: 2, label: '2 — скорее непонятно' },
  { value: 3, label: '3 — частично понятно' },
  { value: 4, label: '4 — в целом понятно' },
  { value: 5, label: '5 — всё понятно' }
] as const;

export const continueUsingOptions = [
  { value: 'yes', label: 'Да, хочу продолжить' },
  { value: 'maybe_after_improvements', label: 'Возможно, если доработаете важные моменты' },
  { value: 'no', label: 'Пока нет' }
] as const;

const primaryTaskValues = primaryTaskOptions.map((option) => option.value) as [string, ...string[]];
const usedSectionValues = usedSectionOptions.map((option) => option.value) as [string, ...string[]];
const mostUsefulFeatureValues = mostUsefulFeatureOptions.map((option) => option.value) as [string, ...string[]];
const missingForDecisionValues = missingForDecisionOptions.map((option) => option.value) as [string, ...string[]];
const continueUsingValues = continueUsingOptions.map((option) => option.value) as [string, ...string[]];

export const demoFeedbackStateSchema = z.object({
  userId: z.string(),
  isDemoUser: z.boolean(),
  accessType: z.number(),
  demoStatus: z.number(),
  hasSubmitted: z.boolean(),
  feedbackId: z.string().nullish(),
  feedbackSubmittedAtUtc: z.string().nullish(),
  canSubmit: z.boolean()
});

export const demoFeedbackFormSchema = z.object({
  primaryTask: z.enum(primaryTaskValues, {
    required_error: 'Выберите задачу, которую пытались решить',
    invalid_type_error: 'Выберите задачу, которую пытались решить'
  }),
  usedSections: z.array(z.enum(usedSectionValues)).min(1, 'Выберите хотя бы один раздел'),
  mostUsefulFeature: z.enum(mostUsefulFeatureValues, {
    required_error: 'Выберите самую полезную функцию',
    invalid_type_error: 'Выберите самую полезную функцию'
  }),
  clarityScore: z.coerce.number().min(1, 'Выберите оценку от 1 до 5').max(5, 'Выберите оценку от 1 до 5'),
  missingForDecision: z.array(z.enum(missingForDecisionValues)).min(1, 'Выберите хотя бы один вариант'),
  generalComment: z.string().trim().min(50, 'Комментарий должен быть не короче 50 символов'),
  continueUsingAnswer: z.enum(continueUsingValues, {
    required_error: 'Выберите один вариант',
    invalid_type_error: 'Выберите один вариант'
  }),
  improvementPriority: z.string().trim().max(1000, 'Опишите доработки короче 1000 символов').optional()
});

export const demoFeedbackSubmitResponseSchema = z.object({
  message: z.string(),
  redirectTo: z.string()
});

export type DemoFeedbackState = z.infer<typeof demoFeedbackStateSchema>;
export type DemoFeedbackFormValues = z.infer<typeof demoFeedbackFormSchema>;
export type DemoFeedbackSubmitResponse = z.infer<typeof demoFeedbackSubmitResponseSchema>;

export async function getDemoFeedbackState(): Promise<DemoFeedbackState> {
  const response = await httpClient<unknown>('/api/demo-feedback');
  return demoFeedbackStateSchema.parse(response);
}

export async function submitDemoFeedback(request: DemoFeedbackFormValues): Promise<DemoFeedbackSubmitResponse> {
  const response = await httpClient<unknown>('/api/demo-feedback', {
    method: 'POST',
    body: request
  });

  return demoFeedbackSubmitResponseSchema.parse(response);
}
