import { describe, expect, it } from "vitest";
import { formatScanResults, type ScanRecord } from "./scanResults";

const base: ScanRecord = { code: "5900000000000", format: "ean_13", msSinceStart: 812.4, msSincePrevious: null, offFound: true, name: "Mleko", brand: "Łaciate" };

describe("formatScanResults", () => {
  it("writes a header and one rounded line per scan", () => {
    const text = formatScanResults([base, { ...base, code: "96385074", format: "ean_8", msSinceStart: 3000, msSincePrevious: 2187.6, offFound: false, name: null, brand: null }]);
    expect(text.split("\n")).toEqual([
      "code\tformat\tms_from_start\tms_from_previous\toff\tname",
      "5900000000000\tean_13\t812\t-\tfound\tŁaciate / Mleko",
      "96385074\tean_8\t3000\t2188\tnot_found\t-"
    ]);
  });

  it("marks a lookup still in flight", () => {
    expect(formatScanResults([{ ...base, offFound: null }])).toContain("\t?\t");
  });
});
