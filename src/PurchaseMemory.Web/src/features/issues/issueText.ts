import type { TFunction } from "i18next";
import type { PurchaseDraftIssue, ReceiptSnapshot } from "../../api/types";

/** Line totals may drift from the receipt total by this much before it counts. */
export const moneyComparisonTolerance = 0.01;

export interface IssueCopy {
  title: string;
  detail: string;
  suggestion: string;
}

/**
 * Localized copy for one issue. The backend also ships English `suggestedFix`
 * prose, but it is only used as a fallback for an issue type this build of the
 * frontend does not know yet.
 */
export function issueCopy(t: TFunction, issue: PurchaseDraftIssue): IssueCopy {
  const recognized = issue.recognizedValue.trim();
  switch (issue.type) {
    case "receiptTotalMismatch":
      return {
        title: t("issues.receiptTotalMismatch.title"),
        detail: t("issues.receiptTotalMismatch.detail", { recognized }),
        suggestion: t("issues.receiptTotalMismatch.suggestion")
      };
    case "missingItemPrice":
      return {
        title: t("issues.missingItemPrice.title", { name: issue.itemName ?? t("issues.unnamedItem") }),
        detail: recognized
          ? t("issues.missingItemPrice.detailPartial", { recognized })
          : t("issues.missingItemPrice.detailEmpty"),
        suggestion: t("issues.missingItemPrice.suggestion")
      };
    default:
      return { title: t("issues.unknown"), detail: recognized, suggestion: issue.suggestedFix };
  }
}

/** A stable key for an issue, since the same type can repeat across items. */
export function issueKey(issue: PurchaseDraftIssue): string {
  return `${issue.type}:${issue.itemPosition ?? "receipt"}`;
}

/**
 * Whether the locally edited snapshot no longer violates the rule that produced
 * this issue. The rules mirror `PurchaseDraftIssueDetector` on the backend so a
 * card can be marked resolved without a round trip; the server stays
 * authoritative and recomputes issues when the review is submitted.
 */
export function isIssueResolved(issue: PurchaseDraftIssue, snapshot: ReceiptSnapshot): boolean {
  if (issue.type === "missingItemPrice") {
    const item = issue.itemPosition === null ? undefined : snapshot.items[issue.itemPosition];
    if (!item) return true;
    return item.unitPrice !== null && item.lineTotal !== null;
  }

  if (issue.type === "receiptTotalMismatch") {
    if (snapshot.receiptTotal === null || snapshot.items.length === 0) return true;
    if (snapshot.items.some((item) => item.lineTotal === null)) return true;
    const linesTotal = snapshot.items.reduce((sum, item) => sum + (item.lineTotal ?? 0), 0);
    return Math.abs(linesTotal - snapshot.receiptTotal) <= moneyComparisonTolerance;
  }

  return false;
}
