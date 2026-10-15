export interface ScanRecord {
  code: string;
  format: string;
  /** Milliseconds from the camera start to this hit, and from the previous hit. */
  msSinceStart: number;
  msSincePrevious: number | null;
  /** null while the Open Food Facts request is in flight. */
  offFound: boolean | null;
  name: string | null;
  brand: string | null;
}

/** One line per scan, tab separated, so the owner can paste the list straight into the chat. */
export function formatScanResults(records: ScanRecord[]): string {
  const header = "code\tformat\tms_from_start\tms_from_previous\toff\tname";
  const rows = records.map((record) => [
    record.code,
    record.format,
    Math.round(record.msSinceStart),
    record.msSincePrevious === null ? "-" : Math.round(record.msSincePrevious),
    record.offFound === null ? "?" : record.offFound ? "found" : "not_found",
    [record.brand, record.name].filter(Boolean).join(" / ") || "-"
  ].join("\t"));
  return [header, ...rows].join("\n");
}
