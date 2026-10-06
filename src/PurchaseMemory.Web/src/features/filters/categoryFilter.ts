import type { ProductCategory } from "../../api/types";

/**
 * The category filter as it may be sent: a link to a category that was since
 * deleted (or a mistyped id) falls back to «all categories» once the tree is
 * known, instead of leaving the page on a load error.
 */
export function knownCategoryFilter(category: string, tree: ProductCategory[] | undefined): string {
  if (category === "" || category === "none" || tree === undefined) return category;
  return tree.some((entry) => entry.id === category) ? category : "";
}

/** Same rule for the tag filter: a deleted or mistyped tag falls back to «all tags». */
export function knownTagFilter(tag: string, tags: { id: string }[] | undefined): string {
  if (tag === "" || tags === undefined) return tag;
  return tags.some((entry) => entry.id === tag) ? tag : "";
}
