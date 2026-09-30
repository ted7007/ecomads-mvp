export function formatPercent(value: number | null | undefined, fractionDigits = 1): string {
  if (value == null || !Number.isFinite(value)) return '—';
  return `${new Intl.NumberFormat('ru-RU', { minimumFractionDigits: fractionDigits, maximumFractionDigits: fractionDigits }).format(value)}%`;
}

