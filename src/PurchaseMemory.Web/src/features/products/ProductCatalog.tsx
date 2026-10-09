import { PackageSearch, SearchX } from "lucide-react";
import { useId } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { BrandSummary, ProductCategory, ProductSummary, TagSummary } from "../../api/types";
import { EmptyState, SkeletonCard, Surface } from "../../design-system/components";
import { CategoryChip } from "../categories/CategoryChip";
import { categoryPath } from "../categories/categoryText";
import { ClassificationFilterFields } from "../filters/ClassificationFilterFields";
import { TagChips } from "../tags/TagChips";
import styles from "./ProductCatalog.module.css";

export interface ProductCatalogProps {
  query: string;
  onQueryChange: (query: string) => void;
  /** The tree for the filter and the chips; the filter hides while it is empty. */
  categories?: ProductCategory[];
  /** Category id, `"none"` for «Без категории», or `""` for any. */
  categoryFilter?: string;
  onCategoryFilterChange?: (value: string) => void;
  brandFilter?: string;
  onBrandFilterChange?: (value: string) => void;
  /** Known brands offered as suggestions in the brand field. */
  brands?: BrandSummary[];
  /** Household tags for the tag filter; the field hides while there are none. */
  tags?: TagSummary[];
  /** Tag id, or `""` for any. */
  tagFilter?: string;
  onTagFilterChange?: (value: string) => void;
  products: ProductSummary[];
  totalCount: number;
  pendingMatchCount: number;
  loading?: boolean;
  error?: string | null;
  hasMore?: boolean;
  loadingMore?: boolean;
  onLoadMore?: () => void;
}

export function ProductCatalog({
  query,
  onQueryChange,
  categories = [],
  categoryFilter = "",
  onCategoryFilterChange,
  brandFilter = "",
  onBrandFilterChange,
  brands = [],
  tags = [],
  tagFilter = "",
  onTagFilterChange,
  products,
  totalCount,
  pendingMatchCount,
  loading = false,
  error = null,
  hasMore = false,
  loadingMore = false,
  onLoadMore
}: ProductCatalogProps) {
  const { t } = useTranslation();
  const groupId = useId();
  const searching = query.trim() !== "" || categoryFilter !== "" || brandFilter.trim() !== "" || tagFilter !== "";
  const resetFilters = () => {
    onQueryChange("");
    onCategoryFilterChange?.("");
    onBrandFilterChange?.("");
    onTagFilterChange?.("");
  };

  return (
    <section className={styles.catalog} aria-labelledby={`${groupId}-title`}>
      <div className={styles.heading}>
        <h2 id={`${groupId}-title`}>{t("products.listTitle")}</h2>
        {!loading && !error && <p className={styles.count}>{t("products.count", { count: totalCount })}</p>}
      </div>

      <Surface as="section" className={styles.filters}>
        <label className={styles.search}>
          <span className={styles.fieldLabel}>{t("products.searchLabel")}</span>
          <input
            type="search"
            value={query}
            placeholder={t("products.searchPlaceholder")}
            onChange={(event) => onQueryChange(event.target.value)}
          />
        </label>
        {onCategoryFilterChange && onBrandFilterChange && (
          <ClassificationFilterFields
            categories={categories}
            brands={brands}
            category={categoryFilter}
            onCategoryChange={onCategoryFilterChange}
            brand={brandFilter}
            onBrandChange={onBrandFilterChange}
            tags={tags}
            tag={tagFilter}
            onTagChange={onTagFilterChange}
          />
        )}
      </Surface>

      {loading && (
        <div aria-busy="true">
          <span className="visually-hidden">{t("products.loading")}</span>
          <ul className={styles.list} aria-hidden="true">
            {Array.from({ length: 4 }, (_, index) => <li key={index}><SkeletonCard /></li>)}
          </ul>
        </div>
      )}

      {!loading && error && <Surface className={styles.notice} role="alert">{error}</Surface>}

      {!loading && !error && products.length === 0 && (
        searching
          ? (
            <EmptyState
              icon={<SearchX />}
              title={t("products.filteredEmptyTitle")}
              description={t("products.filteredEmptyDescription")}
              action={<button className={styles.reset} type="button" onClick={resetFilters}>{t("products.resetSearch")}</button>}
            />
          )
          : (
            <EmptyState
              icon={<PackageSearch />}
              title={t("products.emptyTitle")}
              description={t("products.emptyDescription")}
            />
          )
      )}

      {!loading && !error && products.length > 0 && (
        <>
          <ul className={styles.list}>
            {products.map((product) => (
              <li key={product.id}>
                <Link className={styles.card} to={`/products/${product.id}`}>
                  <Surface as="article" className={styles.cardBody}>
                    <h3 title={product.name}>{product.name}</h3>
                    {(categories.length > 0 || product.brand) && (
                      <p className={styles.classification}>
                        {categories.length > 0 && (
                          <CategoryChip
                            label={categoryPath(t, categories, product.categoryId)}
                            source={product.categorySource ?? null}
                          />
                        )}
                        {product.brand && <span className={styles.brand}>{product.brand}</span>}
                      </p>
                    )}
                    <TagChips tags={product.tags ?? []} />
                    <p className={styles.meta}>
                      <span>{t("products.purchaseCount", { count: product.purchaseCount })}</span>
                      <span aria-hidden="true">·</span>
                      <span>{t("products.aliasCount", { count: product.aliasCount })}</span>
                    </p>
                  </Surface>
                </Link>
              </li>
            ))}
          </ul>

          {hasMore && onLoadMore && (
            <button className={styles.loadMore} type="button" onClick={onLoadMore} disabled={loadingMore}>
              {loadingMore ? t("products.loadingMore") : t("products.loadMore")}
            </button>
          )}
        </>
      )}

      {/* Matching deliberately leaves uncertain lines unlinked. Saying so here
          keeps the catalog honest about being incomplete until the review queue
          of the next stage exists. */}
      {!loading && !error && pendingMatchCount > 0 && (
        <p className={styles.pending}>{t("products.pendingMatches", { count: pendingMatchCount })}</p>
      )}
    </section>
  );
}
