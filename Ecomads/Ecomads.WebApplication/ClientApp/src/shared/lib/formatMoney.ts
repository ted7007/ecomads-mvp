export function formatMoney(value: number | null | undefined): string {
  return `${Math.round(value ?? 0).toLocaleString('ru-RU')} ₽`;
}

