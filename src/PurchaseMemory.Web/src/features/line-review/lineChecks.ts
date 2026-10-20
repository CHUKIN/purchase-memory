import type { ReceiptLineKind } from "../../api/types";
import { moneyComparisonTolerance } from "../issues/issueText";

/** What the checks need from a line (a receipt draft line or a saved purchase line). */
export interface CheckedLineValues {
  quantity: number | null;
  unitPrice: number | null;
  lineTotal: number | null;
  kind?: ReceiptLineKind;
}

/**
 * Why a line deserves a closer look, by the same rules as the backend:
 * a product line without a price or sum (`PurchaseDraftIssueDetector`), or one
 * whose quantity × price is not its sum (the recognizer's arithmetic warning).
 * A discount only needs its amount.
 */
export type LineProblem =
  | { kind: "missingPrice" }
  | { kind: "missingTotal" }
  | { kind: "arithmetic"; expected: number };

export function lineProblem(line: CheckedLineValues): LineProblem | null {
  if (line.kind === "discount") return line.lineTotal === null ? { kind: "missingTotal" } : null;
  if (line.unitPrice === null) return { kind: "missingPrice" };
  if (line.lineTotal === null) return { kind: "missingTotal" };
  if (line.quantity === null) return null;
  const expected = round(line.quantity * line.unitPrice);
  return Math.abs(line.quantity * line.unitPrice - line.lineTotal) > moneyComparisonTolerance
    ? { kind: "arithmetic", expected }
    : null;
}

/** Do the lines add up to the receipt total? Lines without a sum are left out and counted. */
export interface TotalsCheck {
  linesTotal: number;
  receiptTotal: number | null;
  /** Lines minus the receipt total; null without a receipt total. */
  difference: number | null;
  matches: boolean;
  /** Lines without a sum. */
  missing: number;
}

export function totalsCheck(lines: Pick<CheckedLineValues, "lineTotal">[], receiptTotal: number | null): TotalsCheck {
  const linesTotal = round(lines.reduce((sum, line) => sum + (line.lineTotal ?? 0), 0));
  const missing = lines.filter((line) => line.lineTotal === null).length;
  const difference = receiptTotal === null ? null : round(linesTotal - receiptTotal);
  return {
    linesTotal,
    receiptTotal,
    difference,
    matches: difference !== null && Math.abs(difference) <= moneyComparisonTolerance,
    missing
  };
}

/** The first line with a problem, or the first line. */
export function firstProblemIndex(lines: CheckedLineValues[]): number {
  const index = lines.findIndex((line) => lineProblem(line) !== null);
  return index < 0 ? 0 : index;
}

function round(value: number): number {
  return Math.round(value * 100) / 100;
}
