/**
 * Shop barcodes: EAN-13, EAN-8, UPC-A (12 digits) and UPC-E (8 digits).
 * A UPC-A code is stored as the EAN-13 it is equal to (a leading 0), so the
 * same pack scanned as either format finds the same product.
 */

export type BarcodeCheck =
  | { ok: true; code: string }
  | { ok: false; reason: "length" | "checksum" };

/** Digits only, without spaces or dashes a person may type. */
export function digitsOnly(value: string): string {
  return value.replace(/[\s-]/g, "");
}

export function normalizeBarcode(raw: string): string {
  const code = digitsOnly(raw);
  return /^\d{12}$/.test(code) ? `0${code}` : code;
}

/** Validates a typed code: right length and a correct check digit. */
export function checkBarcode(raw: string): BarcodeCheck {
  const code = digitsOnly(raw);
  if (!/^(\d{8}|\d{12}|\d{13})$/.test(code)) return { ok: false, reason: "length" };
  if (!hasValidCheckDigit(code)) return { ok: false, reason: "checksum" };
  return { ok: true, code: normalizeBarcode(code) };
}

/** GTIN check digit: weights 3 and 1 from the right, the sum rounds up to a multiple of 10. */
export function hasValidCheckDigit(code: string): boolean {
  const digits = [...code].map(Number);
  const check = digits.pop();
  if (check === undefined) return false;
  const sum = digits.reverse().reduce((total, digit, index) => total + digit * (index % 2 === 0 ? 3 : 1), 0);
  return (10 - (sum % 10)) % 10 === check;
}

/** «5900512320045» → «5 900512 320045», easier to compare with the print on the pack. */
export function formatBarcode(code: string): string {
  if (code.length === 13) return `${code.slice(0, 1)} ${code.slice(1, 7)} ${code.slice(7)}`;
  if (code.length === 8) return `${code.slice(0, 4)} ${code.slice(4)}`;
  return code;
}
