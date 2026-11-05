import { lookupOpenFoodFactsProduct } from "../../api/client";
import type { OffProductHint } from "../../api/types";

/**
 * Open Food Facts lookup through our server: it sends Open Food Facts only
 * the barcode, waits a few seconds at most and remembers answers. Coverage of
 * Polish products is partial, and the result is a hint only.
 */

export type OffLookup =
  | { status: "found"; product: OffProductHint }
  | { status: "notFound" }
  | { status: "error" };

export async function lookupOpenFoodFacts(code: string, signal?: AbortSignal): Promise<OffLookup> {
  try {
    const answer = await lookupOpenFoodFactsProduct(code, signal);
    if (answer.status === "found" && answer.product) return { status: "found", product: answer.product };
    return answer.status === "notFound" ? { status: "notFound" } : { status: "error" };
  } catch {
    // Offline, a waking server, the per-user limit: the scan goes on without the database.
    return { status: "error" };
  }
}
