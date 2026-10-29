import type { ReviewQueueKind, ReviewQueueSection, ReviewQueueSummary } from "../../api/types";

/**
 * The «Ревью» hub's sections, most important first. The server counts every
 * kind it knows in exactly one of its sections (`ReviewQueueSummary`); the
 * remembered barcodes live on this device only, so the app counts them itself.
 * Names «предложено» (stage 1.4) get a section of their own between products
 * and categories once the server reports them.
 */
export type ReviewSectionId = ReviewQueueSection | "barcodes";

export const reviewSectionOrder: readonly ReviewSectionId[] = ["receipts", "purchases", "products", "categories", "barcodes", "ratings"];

export const sectionKinds: Record<ReviewQueueSection, readonly ReviewQueueKind[]> = {
  receipts: ["failedReceipt", "unsavedReceipt", "totalMismatch", "missingPrice"],
  purchases: ["unapprovedLines"],
  products: ["productNeedsReview"],
  categories: ["suggestedCategory", "missingCategory"],
  ratings: ["unratedPurchase"]
};

/**
 * Sections that can be gone through card by card («Проверять по одной»).
 * Receipts need their full editor and a purchase's lines already are cards
 * (its line review); a barcode needs a product search, so those stay lists.
 */
export const cardSections = ["products", "categories", "ratings"] as const;
export type CardSectionId = (typeof cardSections)[number];

export function isCardSection(value: string | null): value is CardSectionId {
  return value !== null && (cardSections as readonly string[]).includes(value);
}

export function sectionOfKind(kind: ReviewQueueKind): ReviewQueueSection {
  for (const [section, kinds] of Object.entries(sectionKinds) as [ReviewQueueSection, readonly ReviewQueueKind[]][]) {
    if (kinds.includes(kind)) return section;
  }
  return "receipts";
}

/** One section's count from the server summary; 0 when the server has not said. */
export function summaryCount(summary: ReviewQueueSummary | null | undefined, section: ReviewQueueSection): number {
  return summary?.sections.find((entry) => entry.section === section)?.count ?? 0;
}
