import type { TFunction } from "i18next";
import type { ProductCategory } from "../../api/types";

/**
 * Seeded categories are translated by their key; a category the household
 * created or renamed is shown exactly as they wrote it.
 */
export function categoryLabel(t: TFunction, category: Pick<ProductCategory, "name" | "systemKey">): string {
  return category.systemKey
    ? t(`categories.system.${category.systemKey}`, { defaultValue: category.name })
    : category.name;
}

export interface CategoryGroup {
  group: ProductCategory;
  children: ProductCategory[];
}

/** The flat list from the API as groups with their subcategories, in display order. */
export function groupCategories(categories: ProductCategory[]): CategoryGroup[] {
  const byPosition = (left: ProductCategory, right: ProductCategory) => left.position - right.position;
  return categories
    .filter((category) => category.parentId === null)
    .sort(byPosition)
    .map((group) => ({
      group,
      children: categories.filter((category) => category.parentId === group.id).sort(byPosition)
    }));
}

/** «Молочное и яйца / Сыр» for a subcategory, the plain label for a group. */
export function categoryPath(t: TFunction, categories: ProductCategory[], categoryId: string | null | undefined): string | null {
  if (!categoryId) return null;
  const category = categories.find((entry) => entry.id === categoryId);
  if (!category) return null;
  const parent = category.parentId ? categories.find((entry) => entry.id === category.parentId) : undefined;
  return parent ? `${categoryLabel(t, parent)} / ${categoryLabel(t, category)}` : categoryLabel(t, category);
}
