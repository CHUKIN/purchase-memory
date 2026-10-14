/**
 * Formats an amount with its currency. `Intl` throws on a malformed currency
 * code, so anything it rejects falls back to the plain number plus the code
 * instead of breaking the whole card.
 */
export function formatMoney(value: number, currency: string | null | undefined, locale: string): string {
  const plain = new Intl.NumberFormat(locale, { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value);
  if (!currency) return plain;
  try {
    return new Intl.NumberFormat(locale, { style: "currency", currency }).format(value);
  } catch {
    return `${plain} ${currency}`;
  }
}

/** A line quantity: up to three decimals for weighed goods, none for pieces. */
export function formatQuantity(value: number, locale: string): string {
  return new Intl.NumberFormat(locale, { maximumFractionDigits: 3 }).format(value);
}
