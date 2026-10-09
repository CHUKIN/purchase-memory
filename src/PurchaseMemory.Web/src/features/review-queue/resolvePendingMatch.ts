import { getPurchase, updatePurchase } from "../../api/client";
import type { PurchaseDetail, ReviewQueueItem, UpdatePurchaseRequest } from "../../api/types";

/** «Да, это он» links the line to a suggested product; «Создать новый товар» names a new one after the line. */
export type PendingMatchChoice = { productId: string } | "new";

/** The line changed since the queue was loaded (someone else already decided it). */
export class StalePendingMatchError extends Error {
  constructor() {
    super("The receipt line is no longer waiting for a product.");
    this.name = "StalePendingMatchError";
  }
}

/**
 * The purchase as stored, with only the line at `position` relinked. It is the
 * same complete snapshot the purchase editor sends, so the backend treats a
 * one-tap answer from the queue exactly like a choice in the editor's picker:
 * kept links stay as they are, undecided lines go back through the matcher.
 */
export function relinkLineRequest(purchase: PurchaseDetail, position: number, choice: PendingMatchChoice): UpdatePurchaseRequest {
  return {
    expectedUpdatedAt: purchase.updatedAt,
    merchantName: purchase.merchantName,
    purchasedAt: purchase.purchasedAt,
    currency: purchase.currency,
    receiptTotal: purchase.receiptTotal,
    items: purchase.items.map((item) => {
      const discount = item.kind === "discount";
      const target = !discount && item.position === position;
      const productLink = discount
        ? "auto"
        : target
          ? choice === "new" ? "new" : "existing"
          : item.productId ? "existing" : "auto";
      const productId = discount
        ? null
        : target
          ? choice === "new" ? null : choice.productId
          : item.productId;
      return {
        name: item.name,
        quantity: item.quantity,
        unit: item.unit,
        unitPrice: item.unitPrice,
        lineTotal: item.lineTotal,
        kind: item.kind,
        productLink,
        productId: productLink === "existing" ? productId : null,
        boundingBox: item.boundingBox
      };
    })
  };
}

/**
 * Reads the purchase fresh (so `expectedUpdatedAt` is current) and saves it
 * with the queue item's line decided. Refuses when the line is no longer
 * pending, instead of overwriting a decision made elsewhere.
 */
export async function resolvePendingMatch(item: ReviewQueueItem, choice: PendingMatchChoice): Promise<PurchaseDetail> {
  if (!item.purchaseId || item.itemPosition == null) throw new StalePendingMatchError();
  const purchase = await getPurchase(item.purchaseId);
  const line = purchase.items.find((entry) => entry.position === item.itemPosition);
  if (!line || !line.pendingMatch || line.productId) throw new StalePendingMatchError();
  return updatePurchase(purchase.id, relinkLineRequest(purchase, line.position, choice));
}
