import auchan from "../../assets/store-logos/auchan.svg";
import carrefour from "../../assets/store-logos/carrefour.png";
import leclerc from "../../assets/store-logos/leclerc.png";
import lidl from "../../assets/store-logos/lidl.svg";
import pepco from "../../assets/store-logos/pepco.svg";
import rossmann from "../../assets/store-logos/rossmann.png";
import zabka from "../../assets/store-logos/zabka.png";

export interface StoreLogo {
  id: string;
  src: string;
  /** Tested against the normalised merchant name (see normalizeMerchant). */
  pattern: RegExp;
}

/**
 * Chain logos bundled with the app (site icons of the official chain sites,
 * fetched once, no runtime requests). Patterns use word boundaries so a chain
 * name does not match inside another word.
 */
export const storeLogos: ReadonlyArray<StoreLogo> = [
  { id: "carrefour", src: carrefour, pattern: /\bcarrefour\b/ },
  { id: "leclerc", src: leclerc, pattern: /\bleclerc\b/ },
  { id: "rossmann", src: rossmann, pattern: /\brossmann\b/ },
  { id: "lidl", src: lidl, pattern: /\blidl\b/ },
  { id: "zabka", src: zabka, pattern: /\bzabka\b/ },
  { id: "auchan", src: auchan, pattern: /\bauchan\b/ },
  { id: "pepco", src: pepco, pattern: /\bpepco\b/ }
];

/** Lower-case, diacritics folded (ł included), punctuation turned into single spaces. */
export function normalizeMerchant(name: string): string {
  return name
    .normalize("NFD")
    .replace(/\p{M}+/gu, "")
    .replace(/ł/gi, "l")
    .toLowerCase()
    .replace(/[^\p{L}\p{N}]+/gu, " ")
    .trim();
}

/** The bundled logo for a known chain, or null (legal suffixes like "sp. z o.o." do not matter). */
export function findStoreLogo(name: string | null | undefined): StoreLogo | null {
  if (!name) return null;
  const normalized = normalizeMerchant(name);
  return storeLogos.find((logo) => logo.pattern.test(normalized)) ?? null;
}
