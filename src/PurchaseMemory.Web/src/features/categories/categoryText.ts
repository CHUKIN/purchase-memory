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

interface PickerOptionLike { value: string; label: string; detail?: string }
interface PickerGroupLike { title?: string; options: PickerOptionLike[] }

/**
 * The tree as picker sections: one section per group, its own row first
 * (`groupOptionText`, e.g. «Вся группа») and then its subcategories. Inside a
 * section the group name is already the header, so the rows stay short.
 * `leading` rows («Все категории», «Без категории») come first, without a header.
 */
export function categoryPickerGroups(t: TFunction, categories: ProductCategory[], leading: PickerOptionLike[], groupOptionText: string): PickerGroupLike[] {
  return [
    ...(leading.length > 0 ? [{ options: leading }] : []),
    ...groupCategories(categories).map(({ group, children }) => ({
      title: categoryLabel(t, group),
      options: [
        { value: group.id, label: groupOptionText },
        ...children.map((child) => ({ value: child.id, label: categoryLabel(t, child) }))
      ]
    }))
  ];
}

/** A category filter as the row shows it: «Все категории», «Без категории», «Молочное и яйца — вся группа» or a path. */
export function categoryFilterText(t: TFunction, categories: ProductCategory[], value: string): string {
  if (value === "") return t("categories.filterAll");
  if (value === "none") return t("categories.none");
  const category = categories.find((entry) => entry.id === value);
  if (!category) return t("categories.filterAll");
  return category.parentId === null
    ? t("filters.wholeGroup", { name: categoryLabel(t, category) })
    : categoryPath(t, categories, value) ?? "";
}
