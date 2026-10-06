/**
 * Client copy of the server's tag comparison (`ProductNameNormalizer`): case,
 * Polish diacritics, punctuation and extra spaces are ignored, so «lodz»
 * finds an existing «Łódź» instead of asking the server and getting a 409.
 * The server stays the authority; this only avoids a needless round trip.
 */
export function tagKey(name: string): string {
  return name
    .replace(/ł/g, "l")
    .replace(/Ł/g, "L")
    .normalize("NFKD")
    .replace(/\p{Mn}/gu, "")
    .toLowerCase()
    .replace(/ß/g, "ss")
    .replace(/[^\p{L}\p{N}]+/gu, " ")
    .replace(/(?<=\p{N})(?=\p{L})|(?<=\p{L})(?=\p{N})/gu, " ")
    .trim()
    .replace(/\s+/g, " ");
}
