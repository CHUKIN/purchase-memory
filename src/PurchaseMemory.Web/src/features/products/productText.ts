import type { TFunction } from "i18next";

/**
 * Units the backend canonicalizes and this page can name in words. Anything the
 * backend could not fold — an unfamiliar abbreviation from a receipt — is shown
 * verbatim rather than mislabelled.
 */
const namedUnits = ["szt", "kg", "g", "dag", "l", "ml", "m", "opak"] as const;

type NamedUnit = (typeof namedUnits)[number];

function isNamedUnit(unit: string): unit is NamedUnit {
  return (namedUnits as readonly string[]).includes(unit);
}

/** «kilogram» — the full word, for sentences and labels read aloud. */
export function unitName(t: TFunction, unit: string): string {
  return isNamedUnit(unit) ? t(`products.units.${unit}`) : unit;
}

/** «kg» — the abbreviation, for table headers and price suffixes. */
export function unitShortName(t: TFunction, unit: string): string {
  return isNamedUnit(unit) ? t(`products.unitsShort.${unit}`) : unit;
}

/**
 * Prices are stored per currency, but a receipt may not have stated one. The
 * purchase feed already falls back to the household currency, so the product
 * page stays consistent with it instead of inventing a second convention.
 */
export const fallbackCurrency = "PLN";

export function formatMoney(locale: string, currency: string | null, amount: number): string {
  return new Intl.NumberFormat(locale, {
    style: "currency",
    currency: currency ?? fallbackCurrency
  }).format(amount);
}

export function formatDate(locale: string, value: string): string {
  return new Intl.DateTimeFormat(locale, { dateStyle: "medium" }).format(new Date(value));
}
