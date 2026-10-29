import type { AttributeSource } from "../../api/types";

/** Anything that carries a product's receipt text and its human names (stage 1.4). */
export interface NamedProduct {
  /** The receipt text (the catalog's `CanonicalName`). */
  name: string;
  namePl?: string | null;
  namePlSource?: AttributeSource | null;
  nameRu?: string | null;
  nameRuSource?: AttributeSource | null;
}

/**
 * What a product is called on screen: its Polish name, as on the shelf; with
 * no Polish name yet, the receipt text, unmarked.
 */
export function displayName(product: NamedProduct): string {
  return product.namePl?.trim() || product.name;
}

/** The Russian line under the Polish one, or `null` when there is none. */
export function russianName(product: NamedProduct): string | null {
  return product.nameRu?.trim() || null;
}

/** A shown name (Polish or Russian) is still an AI suggestion nobody confirmed. */
export function isNameSuggested(product: NamedProduct): boolean {
  return (Boolean(product.namePl?.trim()) && product.namePlSource === "suggested")
    || (Boolean(product.nameRu?.trim()) && product.nameRuSource === "suggested");
}
