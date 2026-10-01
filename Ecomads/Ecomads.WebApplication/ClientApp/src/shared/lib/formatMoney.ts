export function formatMoney(value: number | null | undefined, fractionDigits = 0): string {
  return `${(fractionDigits ? value ?? 0 : Math.round(value ?? 0)).toLocaleString('ru-RU',
    { minimumFractionDigits: fractionDigits, maximumFractionDigits: fractionDigits })} ₽`;
}
