export function formatPercent(value: number | null | undefined, fractionDigits = 1): string {
  return `${(value ?? 0).toFixed(fractionDigits)}%`;
}

