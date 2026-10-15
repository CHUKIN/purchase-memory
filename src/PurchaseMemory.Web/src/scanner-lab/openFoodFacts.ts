export interface OffResult {
  found: boolean;
  name: string | null;
  brand: string | null;
  quantity: string | null;
  imageUrl: string | null;
  error: string | null;
}

const fields = "product_name,brands,image_front_small_url,quantity";

/** Only the barcode leaves the phone; Open Food Facts needs no key. */
export async function lookupOpenFoodFacts(code: string, signal?: AbortSignal): Promise<OffResult> {
  const empty: OffResult = { found: false, name: null, brand: null, quantity: null, imageUrl: null, error: null };
  try {
    const response = await fetch(`https://world.openfoodfacts.org/api/v2/product/${encodeURIComponent(code)}?fields=${fields}`, { signal });
    // 404 is how Open Food Facts says "no such product".
    if (response.status === 404) return empty;
    if (!response.ok) return { ...empty, error: `HTTP ${response.status}` };
    const body = (await response.json()) as { status?: number; product?: Record<string, string | undefined> };
    if (body.status !== 1 || !body.product) return empty;
    const product = body.product;
    return {
      found: true,
      name: product.product_name?.trim() || null,
      brand: product.brands?.trim() || null,
      quantity: product.quantity?.trim() || null,
      imageUrl: product.image_front_small_url || null,
      error: null
    };
  } catch (error) {
    return { ...empty, error: error instanceof Error ? error.message : "network error" };
  }
}
