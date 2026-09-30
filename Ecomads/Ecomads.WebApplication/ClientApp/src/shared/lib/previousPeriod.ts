import type { DashboardFilters } from '../../pages/DashboardPage/dashboardApi';

export function previousPeriod(filters: DashboardFilters): DashboardFilters {
  const start = new Date(`${filters.startDate}T00:00:00Z`);
  const end = new Date(`${filters.endDate}T00:00:00Z`);
  const days = Math.round((end.getTime() - start.getTime()) / 86400000) + 1;
  const priorEnd = new Date(start); priorEnd.setUTCDate(priorEnd.getUTCDate() - 1);
  const priorStart = new Date(priorEnd); priorStart.setUTCDate(priorStart.getUTCDate() - days + 1);
  return { startDate: priorStart.toISOString().slice(0, 10), endDate: priorEnd.toISOString().slice(0, 10) };
}
