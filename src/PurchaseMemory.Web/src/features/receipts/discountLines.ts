import type { ReceiptItemSnapshot } from "../../api/types";

type LinkLine = Pick<ReceiptItemSnapshot, "kind"> & Partial<Pick<ReceiptItemSnapshot, "name" | "lineTotal">>;

export interface DiscountLink {
  /** Zero-based position of the product the discount reduces, if any. */
  target: number | null;
  /** A discount on the whole receipt: it has no product by design. */
  appliesToReceipt: boolean;
}

// Mirrors ReceiptDiscountLinking on the backend.
const receiptLevelMarkers = ["podsuma", "suma", "na paragon", "na cały", "na caly", "na całe", "na cale", "całość", "calosc"];

export const isDiscount = (item: Pick<ReceiptItemSnapshot, "kind">): boolean => item.kind === "discount";

export const hasReceiptLevelMarker = (name: string | undefined): boolean => {
  const lower = (name ?? "").toLocaleLowerCase("pl");
  return receiptLevelMarkers.some((marker) => lower.includes(marker));
};

/**
 * Where each discount belongs, by the same rule as the backend: the nearest
 * product above it, unless the discount is for the whole receipt (marked as
 * such, or larger than that product). Recomputed on every edit so the draft
 * stays right while the user adds or deletes rows before the next save.
 */
export function discountLinks(items: LinkLine[]): DiscountLink[] {
  let lastProduct: number | null = null;
  return items.map((item, position) => {
    if (!isDiscount(item)) {
      lastProduct = position;
      return { target: null, appliesToReceipt: false };
    }
    const productTotal = lastProduct === null ? null : items[lastProduct].lineTotal ?? null;
    const receiptLevel =
      hasReceiptLevelMarker(item.name) ||
      (productTotal !== null && item.lineTotal != null && Math.abs(item.lineTotal) > productTotal);
    return receiptLevel ? { target: null, appliesToReceipt: true } : { target: lastProduct, appliesToReceipt: false };
  });
}
