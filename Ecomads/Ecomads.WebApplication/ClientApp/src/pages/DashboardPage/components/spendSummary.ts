export function spendSummary(days: readonly { date: string; spend: number | null }[]) {
  const confirmed = days.filter(day => day.spend !== null);
  return {
    total: confirmed.length ? confirmed.reduce((sum, day) => sum + day.spend!, 0) : null,
    confirmedDays: confirmed.length,
    expectedDays: days.length,
    complete: days.length > 0 && confirmed.length === days.length,
    missingDates: days.filter(day => day.spend === null).map(day => day.date)
  };
}
