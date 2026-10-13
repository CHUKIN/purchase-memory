import { useTranslation } from "react-i18next";
import type { BrandSummary, ProductCategory, TagSummary } from "../../api/types";
import { SelectRow } from "../../design-system/components";
import { categoryFilterText, categoryPickerGroups } from "../categories/categoryText";

/**
 * The category, brand and tag filters shared by the purchase feed, the
 * catalog and the «Нужно проверить» queue: list rows in a filter sheet, each
 * opening a full-screen list with a checkmark on the current choice.
 * Callers hide a row while its list (tree, tags) is not loaded or empty.
 */
export function CategoryFilterRow({ categories, category, onCategoryChange }: {
  categories: ProductCategory[];
  /** Category id, `"none"` for «Без категории», or `""` for any. */
  category: string;
  onCategoryChange: (value: string) => void;
}) {
  const { t } = useTranslation();
  return (
    <SelectRow
      label={t("categories.filterLabel")}
      valueText={categoryFilterText(t, categories, category)}
      value={category}
      onChange={onCategoryChange}
      searchable
      groups={categoryPickerGroups(t, categories, [
        { value: "", label: t("categories.filterAll") },
        { value: "none", label: t("categories.none") }
      ], t("filters.wholeGroupShort"))}
    />
  );
}

/**
 * Brand: the known brands to pick from, plus the typed text itself, so part
 * of a name («mleko») still filters by substring as before.
 */
export function BrandFilterRow({ brands, brand, onBrandChange }: {
  brands: BrandSummary[];
  brand: string;
  onBrandChange: (value: string) => void;
}) {
  const { t } = useTranslation();
  return (
    <SelectRow
      label={t("categories.brandFilterLabel")}
      valueText={brand.trim() || t("filters.anyBrand")}
      value={brand}
      onChange={onBrandChange}
      searchable
      searchPlaceholder={t("categories.brandFilterPlaceholder")}
      freeText={(text) => brands.some((entry) => entry.brand === text) ? null : { value: text, label: t("filters.brandContains", { text }) }}
      groups={[
        { options: [{ value: "", label: t("filters.anyBrand") }] },
        ...(brands.length > 0
          ? [{
            title: t("filters.knownBrands"),
            options: brands.map((entry) => ({ value: entry.brand, label: entry.brand, detail: t("filters.brandProducts", { count: entry.productCount }) }))
          }]
          : [])
      ]}
    />
  );
}

export function TagFilterRow({ tags, tag, onTagChange }: {
  /** Household tags, alphabetical as the API returns them. */
  tags: TagSummary[];
  /** Tag id, or `""` for any. */
  tag: string;
  onTagChange: (value: string) => void;
}) {
  const { t } = useTranslation();
  return (
    <SelectRow
      label={t("tags.filterLabel")}
      valueText={tags.find((entry) => entry.id === tag)?.name ?? t("tags.filterAll")}
      value={tag}
      onChange={onTagChange}
      searchable={tags.length > 8}
      groups={[{ options: [{ value: "", label: t("tags.filterAll") }, ...tags.map((entry) => ({ value: entry.id, label: entry.name }))] }]}
    />
  );
}
