import type { z } from 'zod';
import type { loadedPeriodSchema, projectDashboardSchema, projectKpiSchema } from './apiSchemas';

export type ProjectKpi = z.infer<typeof projectKpiSchema>;
export type ProjectDashboard = z.infer<typeof projectDashboardSchema>;
export type LoadedPeriod = z.infer<typeof loadedPeriodSchema>;
