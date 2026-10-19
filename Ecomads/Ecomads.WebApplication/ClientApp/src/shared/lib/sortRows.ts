export type SortValue = string | number | null | undefined;

/** Never mutate query data. Missing/non-finite values stay last in both directions. */
export function sortRows<T>(rows: readonly T[], value: (row: T) => SortValue, ascending: boolean, id: (row: T) => string): T[] {
  const normalize = (v: SortValue) => typeof v === 'number' && !Number.isFinite(v) ? null : v ?? null;
  return [...rows].sort((left, right) => {
    const a = normalize(value(left)), b = normalize(value(right));
    if (a === null || b === null) return a === b ? id(left).localeCompare(id(right)) : a === null ? 1 : -1;
    const order = typeof a === 'string' && typeof b === 'string' ? a.localeCompare(b, 'ru') : Number(a) - Number(b);
    return (ascending ? order : -order) || id(left).localeCompare(id(right));
  });
}
