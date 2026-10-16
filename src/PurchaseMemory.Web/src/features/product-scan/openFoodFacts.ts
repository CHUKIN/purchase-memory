import type { OffProductHint } from "./productMediaStore";

/**
 * Open Food Facts lookup, straight from the phone for now (stage 2 moves it
 * behind our backend with a cache). Only the barcode leaves the device; no
 * key is needed. Coverage of Polish products is partial, and the result is a
 * hint only.
 */

export type OffLookup =
  | { status: "found"; product: OffProductHint }
  | { status: "notFound" }
  | { status: "error" };

const fields = "product_name,brands,image_front_small_url,quantity";

/** A slow answer in the shop is no answer: after this the result says the database is not answering. */
export const offTimeoutMs = 6000;

export async function lookupOpenFoodFacts(code: string, signal?: AbortSignal, timeoutMs = offTimeoutMs): Promise<OffLookup> {
  const { signal: limited, dispose } = withTimeout(signal, timeoutMs);
  try {
    const response = await fetch(`https://world.openfoodfacts.org/api/v2/product/${encodeURIComponent(code)}?fields=${fields}`, { signal: limited });
    // 404 is how Open Food Facts says "no such product".
    if (response.status === 404) return { status: "notFound" };
    if (!response.ok) return { status: "error" };
    const body = (await response.json()) as { status?: number; product?: Record<string, string | undefined> };
    if (body.status !== 1 || !body.product) return { status: "notFound" };
    const product = body.product;
    const hint: OffProductHint = {
      name: product.product_name?.trim() || null,
      brand: product.brands?.trim() || null,
      quantity: product.quantity?.trim() || null,
      imageUrl: safeImageUrl(product.image_front_small_url)
    };
    return hint.name || hint.brand ? { status: "found", product: hint } : { status: "notFound" };
  } catch {
    return { status: "error" };
  } finally {
    dispose();
  }
}

/** The caller's signal (react-query cancels on unmount) plus a timeout; `AbortSignal.any` is missing on older iOS. */
function withTimeout(signal: AbortSignal | undefined, timeoutMs: number): { signal: AbortSignal; dispose: () => void } {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(new DOMException("Open Food Facts did not answer in time.", "TimeoutError")), timeoutMs);
  const onAbort = () => controller.abort(signal?.reason);
  if (signal?.aborted) onAbort();
  else signal?.addEventListener("abort", onAbort, { once: true });
  return {
    signal: controller.signal,
    dispose: () => {
      clearTimeout(timer);
      signal?.removeEventListener("abort", onAbort);
    }
  };
}

/** Only https images from Open Food Facts' own hosts are shown. */
function safeImageUrl(value: string | undefined): string | null {
  if (!value) return null;
  try {
    const url = new URL(value);
    return url.protocol === "https:" && /(^|\.)openfoodfacts\.org$/.test(url.hostname) ? url.toString() : null;
  } catch {
    return null;
  }
}
