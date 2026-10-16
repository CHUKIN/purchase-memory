import { describe, expect, it } from "vitest";
import { checkBarcode, formatBarcode, hasValidCheckDigit, normalizeBarcode } from "./barcode";

describe("shop barcodes", () => {
  it("accepts EAN-13, EAN-8 and UPC-A with a correct check digit", () => {
    expect(checkBarcode("5900512320045")).toEqual({ ok: true, code: "5900512320045" });
    expect(checkBarcode("96385074")).toEqual({ ok: true, code: "96385074" });
    // UPC-A is kept as the EAN-13 it equals, so both scans find one product.
    expect(checkBarcode("036000291452")).toEqual({ ok: true, code: "0036000291452" });
  });

  it("ignores spaces and dashes a person types", () => {
    expect(checkBarcode(" 5 900512-320045 ")).toEqual({ ok: true, code: "5900512320045" });
  });

  it("names the problem: wrong length or a typo caught by the check digit", () => {
    expect(checkBarcode("12345")).toEqual({ ok: false, reason: "length" });
    expect(checkBarcode("59005123200a5")).toEqual({ ok: false, reason: "length" });
    expect(checkBarcode("5900512320046")).toEqual({ ok: false, reason: "checksum" });
    expect(hasValidCheckDigit("")).toBe(false);
  });

  it("normalises only 12-digit codes", () => {
    expect(normalizeBarcode("036000291452")).toBe("0036000291452");
    expect(normalizeBarcode("5900512320045")).toBe("5900512320045");
    expect(normalizeBarcode("96385074")).toBe("96385074");
  });

  it("groups digits the way they are printed", () => {
    expect(formatBarcode("5900512320045")).toBe("5 900512 320045");
    expect(formatBarcode("96385074")).toBe("9638 5074");
    expect(formatBarcode("123")).toBe("123");
  });
});
