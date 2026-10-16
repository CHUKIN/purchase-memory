import { describe, expect, it } from "vitest";
import { checkBarcode, expandUpcE, formatBarcode, legacyUpcECodes, hasValidCheckDigit, normalizeBarcode } from "./barcode";

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

  it("expands UPC-E to the UPC-A / EAN-13 it equals", () => {
    // 01234565 is the textbook UPC-E of 012345000065.
    expect(expandUpcE("01234565")).toBe("012345000065");
    expect(normalizeBarcode("01234565")).toBe("0012345000065");
    expect(normalizeBarcode("012345000065")).toBe("0012345000065");
    expect(checkBarcode("01234565")).toEqual({ ok: true, code: "0012345000065" });
    // Every expansion rule (last middle digit 0-2, 3, 4, 5-9).
    expect(expandUpcE("01234505")).toBe("012000003455");
    expect(expandUpcE("01234531")).toBe("012300000451");
    expect(expandUpcE("01234543")).toBe("012340000053");
  });

  it("finds the 8-digit UPC-E a normalized code could have been saved as", () => {
    expect(legacyUpcECodes("0012345000065")).toEqual(["01234565"]);
    expect(legacyUpcECodes("0012000003455")).toEqual(["01234505"]);
    expect(legacyUpcECodes("5900512320045")).toEqual([]);
  });

  it("leaves non-UPC-E 8-digit codes alone", () => {
    // Starts with 9, and a 0-prefixed code whose expansion has a wrong check digit.
    expect(expandUpcE("96385074")).toBeNull();
    expect(normalizeBarcode("96385074")).toBe("96385074");
    expect(normalizeBarcode("01234566")).toBe("01234566");
    expect(checkBarcode("01234566")).toEqual({ ok: false, reason: "checksum" });
  });

  it("groups digits the way they are printed", () => {
    expect(formatBarcode("5900512320045")).toBe("5 900512 320045");
    expect(formatBarcode("96385074")).toBe("9638 5074");
    expect(formatBarcode("123")).toBe("123");
  });
});
