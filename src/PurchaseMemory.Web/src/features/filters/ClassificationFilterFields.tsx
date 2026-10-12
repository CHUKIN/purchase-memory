import { useId } from "react";
import { useTranslation } from "react-i18next";
import type { BrandSummary, ProductCategory, TagSummary } from "../../api/types";
import { CategoryOptions } from "../categories/CategoryOptions";
import styles from "./ClassificationFilterFields.module.css";

export interface ClassificationFilterFieldsProps {
  categories: ProductCategory[];
  brands?: BrandSummary[];
  /** Category id, `"none"` for «Без категории», or `""` for any. */
  category: string;
  onCategoryChange: (value: string) => void;
  brand: string;
  onBrandChange: (value: string) => void;
  /** Household tags; the tag field hides while there are none. */
  tags?: TagSummary[];
  /** Tag id, or `""` for any. */
  tag?: string;
  onTagChange?: (value: string) => void;
}

/**
 * Category, brand and tag filters shared by the purchase feed and the catalog.
 * The category is a native select with one option group per top-level group
 * (the group itself selectable first, and it includes its subcategories), so
 * keyboard, screen readers and the phone's wheel picker work without custom
 * ARIA. The brand is a text field with the known brands offered as native
 * suggestions: picking one or typing part of a name both filter. The tag is
 * a plain native select of the household's tags.
 */
export function ClassificationFilterFields({
  categories,
  brands = [],
  category,
  onCategoryChange,
  brand,
  onBrandChange,
  tags = [],
  tag = "",
  onTagChange
}: ClassificationFilterFieldsProps) {
  const { t } = useTranslation();
  const id = useId();
  const brandListId = `${id}-brands`;
  const showTags = tags.length > 0 && onTagChange !== undefined;

  return (
    <div className={styles.row} data-with-tags={showTags || undefined}>
      {categories.length > 0 && <CategoryFilterField categories={categories} category={category} onCategoryChange={onCategoryChange} />}
      <div className={styles.field}>
        <label className={styles.label} htmlFor={`${id}-brand`}>{t("categories.brandFilterLabel")}</label>
        <input
          id={`${id}-brand`}
          className={styles.control}
          type="search"
          value={brand}
          maxLength={100}
          autoComplete="off"
          list={brands.length > 0 ? brandListId : undefined}
          placeholder={t("categories.brandFilterPlaceholder")}
          onChange={(event) => onBrandChange(event.target.value)}
        />
        {brands.length > 0 && (
          <datalist id={brandListId}>
            {brands.map((entry) => (
              <option key={entry.brand} value={entry.brand}>
                {t("filters.brandProducts", { count: entry.productCount })}
              </option>
            ))}
          </datalist>
        )}
      </div>
      {showTags && <TagFilterField tags={tags} tag={tag} onTagChange={onTagChange} />}
    </div>
  );
}

export interface CategoryFilterFieldProps {
  categories: ProductCategory[];
  /** Category id, `"none"` for «Без категории», or `""` for any. */
  category: string;
  onCategoryChange: (value: string) => void;
}

/**
 * The «Категория» select on its own: part of the classification row in the
 * feed and catalog, and one of the two filters of the «Нужно проверить» queue.
 * Callers hide it while the category tree is not loaded.
 */
export function CategoryFilterField({ categories, category, onCategoryChange }: CategoryFilterFieldProps) {
  const { t } = useTranslation();
  const id = useId();

  return (
    <div className={styles.field}>
      <label className={styles.label} htmlFor={`${id}-category`}>{t("categories.filterLabel")}</label>
      <select
        id={`${id}-category`}
        className={styles.control}
        value={category}
        onChange={(event) => onCategoryChange(event.target.value)}
      >
        <option value="">{t("categories.filterAll")}</option>
        <option value="none">{t("categories.none")}</option>
        <CategoryOptions
          categories={categories}
          selected={category}
          groupOptionText={t("filters.wholeGroupShort")}
          selectedGroupText={(name) => t("filters.wholeGroup", { name })}
        />
      </select>
    </div>
  );
}

export interface TagFilterFieldProps {
  /** Household tags, alphabetical as the API returns them. */
  tags: TagSummary[];
  /** Tag id, or `""` for any. */
  tag: string;
  onTagChange: (value: string) => void;
}

/**
 * The «Тег» select on its own: part of the classification row in the feed and
 * catalog, and one of the two filters of the «Нужно проверить» queue. Callers
 * hide it while the household has no tags.
 */
export function TagFilterField({ tags, tag, onTagChange }: TagFilterFieldProps) {
  const { t } = useTranslation();
  const id = useId();

  return (
    <div className={styles.field}>
      <label className={styles.label} htmlFor={`${id}-tag`}>{t("tags.filterLabel")}</label>
      <select
        id={`${id}-tag`}
        className={styles.control}
        value={tag}
        onChange={(event) => onTagChange(event.target.value)}
      >
        <option value="">{t("tags.filterAll")}</option>
        {tags.map((entry) => (
          <option key={entry.id} value={entry.id}>{entry.name}</option>
        ))}
      </select>
    </div>
  );
}
