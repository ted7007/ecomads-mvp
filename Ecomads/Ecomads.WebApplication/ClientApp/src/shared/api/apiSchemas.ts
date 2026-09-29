import { z } from 'zod';

export const projectKpiSchema = z.object({
  spend: z.coerce.number(), revenue: z.coerce.number(), orderedAmount: z.coerce.number(),
  drr: z.coerce.number(), clicks: z.coerce.number(), impressions: z.coerce.number(), ctr: z.coerce.number()
});
export const projectDashboardSchema = z.object({
  id: z.string(), name: z.string(), kpi: projectKpiSchema, targetDrr: z.coerce.number()
});
export const projectsResponseSchema = z.array(projectDashboardSchema);
export const loadedPeriodSchema = z.object({ startDate: z.string(), endDate: z.string() });
export const loadedPeriodsResponseSchema = z.array(loadedPeriodSchema);
