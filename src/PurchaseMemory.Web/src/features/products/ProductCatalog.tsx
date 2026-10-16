import { IonButton, IonItem, IonLabel, IonList, IonSearchbar, IonToggle } from "@ionic/react";
import { CircleAlert, PackageSearch, SearchX } from "lucide-react";
import { useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import type { BrandSummary, ProductCategory, ProductSummary, TagSummary } from "../../api/types";
import { EmptyState, FilterButton, ItemLink, LoadingList, Notice, Page, SectionHeader, Sheet } from "../../design-system/components";
import { CategoryChip } from "../categories/CategoryChip";
import { HelpLink } from "../help/HelpLink";
import { ProductThumb } from "../product-scan/ProductPhoto";
import { useAnyMainPhoto } from "../product-scan/useProductMedia";
import { categoryPath } from "../categories/categoryText";
import { BrandFilterRow, CategoryFilterRow, TagFilterRow } from "../filters/FilterRows";
import { TagChips } from "../tags/TagChips";
import { useReturnState } from "../../app/backNavigation";
import { ReviewFlagChip } from "./ReviewFlagChip";

export interface ProductCatalogProps {
  query: string;
  onQueryChange: (query: string) => void;
  /** The tree for the filter and the chips; the filter row hides while it is empty. */
  categories?: ProductCategory[];
  /** Category id, `"none"` for «Без категории», or `""` for any. */
  categoryFilter?: string;
  onCategoryFilterChange?: (value: string) => void;
  brandFilter?: string;
  onBrandFilterChange?: (value: string) => void;
  /** Known brands offered in the brand filter. */
  brands?: BrandSummary[];
  /** Household tags for the tag filter; the row hides while there are none. */
  tags?: TagSummary[];
  /** Tag id, or `""` for any. */
  tagFilter?: string;
  onTagFilterChange?: (value: string) => void;
  /** Only products flagged «проверить». */
  reviewFilter?: boolean;
  onReviewFilterChange?: (value: boolean) => void;
  products: ProductSummary[];
  totalCount: number;
  /** Flagged products in the whole catalog; the tappable hint hides at 0. */
  needsReviewCount: number;
  loading?: boolean;
  error?: string | null;
  hasMore?: boolean;
  loadingMore?: boolean;
  onLoadMore?: () => void;
  /** Rows under the list, e.g. managing categories and tags. */
  footer?: ReactNode;
  /** Opens with the filter sheet up; for stories. */
  initialFiltersOpen?: boolean;
}

/**
 * «Товары»: search under the large title, category/brand/tag in the filter
 * sheet, and every product as a row with its category and tags as chips.
 */
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
  reviewFilter = false,
  onReviewFilterChange,
  products,
  totalCount,
  needsReviewCount,
  loading = false,
  error = null,
  hasMore = false,
  loadingMore = false,
  onLoadMore,
  footer,
  initialFiltersOpen = false
}: ProductCatalogProps) {
  const { t } = useTranslation();
  const returnState = useReturnState();
  const [filtersOpen, setFiltersOpen] = useState(initialFiltersOpen);
  // One photo in the list gives every row a thumbnail slot, so names line up.
  const anyPhoto = useAnyMainPhoto(products.map((product) => product.id));
  const sheetCount = [categoryFilter !== "", brandFilter.trim() !== "", tagFilter !== "", reviewFilter].filter(Boolean).length;
  const searching = query.trim() !== "" || sheetCount > 0;
  const filterable = onCategoryFilterChange !== undefined && onBrandFilterChange !== undefined;
  const resetSheet = () => {
    onCategoryFilterChange?.("");
    onBrandFilterChange?.("");
    onTagFilterChange?.("");
    onReviewFilterChange?.(false);
  };
  const resetFilters = () => {
    onQueryChange("");
    resetSheet();
  };

  return (
    <Page
      title={t("products.pageTitle")}
      end={<><HelpLink topic="products" />{filterable && <FilterButton count={sheetCount} onClick={() => setFiltersOpen(true)} />}</>}
      search={(
        <IonSearchbar
          value={query}
          placeholder={t("products.searchPlaceholder")}
          aria-label={t("products.searchLabel")}
          onIonInput={(event) => onQueryChange(event.detail.value ?? "")}
        />
      )}
    >
      <SectionHeader>{!loading && !error ? t("products.count", { count: totalCount }) : "\u00a0"}</SectionHeader>

      {loading && <LoadingList label={t("products.loading")} />}
      {!loading && error && <Notice tone="danger" role="alert"><p>{error}</p></Notice>}

      {/* Doubtful matches became products of their own; this is the way to them. */}
      {!loading && !error && needsReviewCount > 0 && !reviewFilter && onReviewFilterChange && (
        <IonList inset>
          <IonItem button detail onClick={() => onReviewFilterChange(true)}>
            <CircleAlert slot="start" size={22} className="pm-row-icon pm-warning-icon" aria-hidden="true" />
            <IonLabel className="pm-wrap">{t("products.needsReviewCount", { count: needsReviewCount })}</IonLabel>
          </IonItem>
        </IonList>
      )}

      {!loading && !error && products.length === 0 && (
        searching
          ? (
            <EmptyState
              icon={<SearchX />}
              title={t("products.filteredEmptyTitle")}
              action={<IonButton fill="clear" onClick={resetFilters}>{t("products.resetSearch")}</IonButton>}
            />
          )
          : <EmptyState icon={<PackageSearch />} title={t("products.emptyTitle")} />
      )}

      {!loading && !error && products.length > 0 && (
        <>
          <IonList inset className="pm-after-header">
            {products.map((product) => (
              <ItemLink key={product.id} to={`/products/${product.id}`} state={returnState}>
                <ProductThumb productId={product.id} placeholder={anyPhoto} />
                <IonLabel className="pm-wrap">
                  <h2>{product.name}</h2>
                  {(categories.length > 0 || (product.tags ?? []).length > 0 || product.needsReview) && (
                    <div className="pm-chip-row">
                      {product.needsReview && <ReviewFlagChip />}
                      {categories.length > 0 && <CategoryChip label={categoryPath(t, categories, product.categoryId)} source={product.categorySource ?? null} />}
                      <TagChips tags={product.tags ?? []} />
                    </div>
                  )}
                  <p>
                    {[product.brand, t("products.purchaseCount", { count: product.purchaseCount })]
                      .filter(Boolean).join(" · ")}
                  </p>
                </IonLabel>
              </ItemLink>
            ))}
          </IonList>
          {hasMore && onLoadMore && (
            <div className="ion-padding-horizontal">
              <IonButton expand="block" fill="clear" onClick={onLoadMore} disabled={loadingMore}>
                {loadingMore ? t("products.loadingMore") : t("products.loadMore")}
              </IonButton>
            </div>
          )}
        </>
      )}

      {footer}

      {filterable && (
        <Sheet
          isOpen={filtersOpen}
          onClose={() => setFiltersOpen(false)}
          title={t("ui.filters")}
          start={<IonButton disabled={sheetCount === 0} onClick={resetSheet}>{t("ui.reset")}</IonButton>}
          initialBreakpoint={0.6}
          help={<HelpLink topic="filters" />}
        >
          <IonList inset>
            {categories.length > 0 && <CategoryFilterRow categories={categories} category={categoryFilter} onCategoryChange={onCategoryFilterChange} />}
            <BrandFilterRow brands={brands} brand={brandFilter} onBrandChange={onBrandFilterChange} />
            {tags.length > 0 && onTagFilterChange && <TagFilterRow tags={tags} tag={tagFilter} onTagChange={onTagFilterChange} />}
            {onReviewFilterChange && (
              <IonItem>
                <IonToggle checked={reviewFilter} onIonChange={(event) => onReviewFilterChange(event.detail.checked)}>
                  {t("products.needsReviewFilter")}
                </IonToggle>
              </IonItem>
            )}
          </IonList>
        </Sheet>
      )}
    </Page>
  );
}
