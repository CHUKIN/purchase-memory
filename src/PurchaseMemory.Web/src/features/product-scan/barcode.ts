/**
 * Shop barcodes: EAN-13, EAN-8, UPC-A (12 digits) and UPC-E (8 digits).
 * A UPC-A code is stored as the EAN-13 it is equal to (a leading 0), and a
 * UPC-E is expanded to UPC-A first, so the same pack scanned as any of these
 * formats finds the same product.
 */

export type BarcodeCheck =
  | { ok: true; code: string }
  | { ok: false; reason: "length" | "checksum" };

/** Digits only, without spaces or dashes a person may type. */
export function digitsOnly(value: string): string {
  return value.replace(/[\s-]/g, "");
}

/**
 * Expands an 8-digit UPC-E (number system 0 or 1) to the 12-digit UPC-A it
 * stands for, or returns null when the code is not a valid UPC-E. UPC-E and
 * EAN-8 are both 8 digits; the UPC-E check digit is the one of the expanded
 * UPC-A, so a code whose expansion does not check out is left alone.
 */
export function expandUpcE(code: string): string | null {
  if (!/^[01]\d{7}$/.test(code)) return null;
  const system = code[0];
  const [d1, d2, d3, d4, d5, d6] = code.slice(1, 7);
  const check = code[7];
  let body: string;
  if (d6 === "0" || d6 === "1" || d6 === "2") body = `${d1}${d2}${d6}0000${d3}${d4}${d5}`;
  else if (d6 === "3") body = `${d1}${d2}${d3}00000${d4}${d5}`;
  else if (d6 === "4") body = `${d1}${d2}${d3}${d4}00000${d5}`;
  else body = `${d1}${d2}${d3}${d4}${d5}0000${d6}`;
  const upcA = `${system}${body}${check}`;
  return hasValidCheckDigit(upcA) ? upcA : null;
}

/**
 * The 8-digit UPC-E a normalized EAN-13 (leading 0) could have been saved as
 * before UPC-E was expanded; empty for other codes. Used to find old records.
 */
export function legacyUpcECodes(ean13: string): string[] {
  if (!/^0\d{12}$/.test(ean13)) return [];
  const a = ean13.slice(1);
  const system = a[0];
  const check = a[11];
  const zero = (from: number, to: number) => a.slice(from, to) === "0".repeat(to - from);
  const candidates: string[] = [];
  if ("012".includes(a[3]) && zero(4, 8)) candidates.push(`${system}${a.slice(1, 3)}${a.slice(8, 11)}${a[3]}${check}`);
  if (zero(4, 9)) candidates.push(`${system}${a.slice(1, 4)}${a.slice(9, 11)}3${check}`);
  if (zero(5, 10)) candidates.push(`${system}${a.slice(1, 5)}${a[10]}4${check}`);
  if (zero(6, 10) && a[10] >= "5") candidates.push(`${system}${a.slice(1, 6)}${a[10]}${check}`);
  return candidates.filter((code) => expandUpcE(code) === a);
}

/** Digits only; UPC-A and UPC-E become the EAN-13 they equal, so one product has one code. */
export function normalizeBarcode(raw: string): string {
  const code = digitsOnly(raw);
  if (/^\d{12}$/.test(code)) return `0${code}`;
  const upcA = expandUpcE(code);
  return upcA ? `0${upcA}` : code;
}

/** Validates a typed code: right length and a correct check digit. */
export function checkBarcode(raw: string): BarcodeCheck {
  const code = digitsOnly(raw);
  if (!/^(\d{8}|\d{12}|\d{13})$/.test(code)) return { ok: false, reason: "length" };
  // An 8-digit code is valid as EAN-8 or as UPC-E (checked on its expansion).
  if (!hasValidCheckDigit(code) && !(code.length === 8 && expandUpcE(code))) return { ok: false, reason: "checksum" };
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
