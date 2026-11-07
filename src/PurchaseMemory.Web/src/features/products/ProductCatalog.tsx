import { IonButton, IonItem, IonLabel, IonList, IonSearchbar, IonToggle } from "@ionic/react";
import { CircleAlert, PackageSearch, ScanBarcode, SearchX, Sparkles } from "lucide-react";
import { useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import type { BrandSummary, ProductCategory, ProductSummary, TagSummary } from "../../api/types";
import { EmptyState, FilterButton, ItemLink, LoadingList, Notice, Page, SectionHeader, Sheet } from "../../design-system/components";
import { CategoryTile, categoryGroupKey } from "../categories/CategoryTile";
import { HelpLink } from "../help/HelpLink";
import { ProductThumb } from "../product-scan/ProductPhoto";
import { categoryLabel } from "../categories/categoryText";
import { BrandFilterRow, CategoryFilterRow, TagFilterRow } from "../filters/FilterRows";
import { TagChips } from "../tags/TagChips";
import { useNavigate } from "react-router";
import { useOriginBack, useReturnState } from "../../app/backNavigation";
import { displayName, isNameSuggested, russianName } from "./productNameText";

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
 * sheet, and every product as a row: its photo or its category's tile, the
 * name, and «подкатегория · бренд · N покупок» with its tags as chips.
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
  const navigate = useNavigate();
  // A tab root has no back button, except when a detail screen sent the person here («Товары в этой категории»).
  const back = useOriginBack();
  const [filtersOpen, setFiltersOpen] = useState(initialFiltersOpen);
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
      back={back}
      end={<><HelpLink topic="products" /><IonButton className="pm-icon-button" aria-label={t("productScan.homeScanButton")} onClick={() => navigate("/scan", { state: returnState })}><ScanBarcode size={22} aria-hidden="true" /></IonButton>{filterable && <FilterButton count={sheetCount} onClick={() => setFiltersOpen(true)} />}</>}
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
          <IonList inset className="pm-after-header pm-catalog-list">
            {products.map((product) => (
              <ItemLink key={product.id} to={`/products/${product.id}`} state={returnState}>
                <ProductLeading photoId={product.mainPhotoId} group={categories.length > 0 ? categoryGroupKey(categories, product.categoryId) : null} />
                <IonLabel className="pm-wrap">
                  <h2><ProductTitle product={product} /></h2>
                  <p className="pm-product-meta"><ProductMeta product={product} categories={categories} /></p>
                  {(product.tags ?? []).length > 0 && <div className="pm-chip-row"><TagChips tags={product.tags ?? []} /></div>}
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

/** The product's main photo when it has one, else its category's tile. */
function ProductLeading({ photoId, group }: { photoId: string | null | undefined; group: string | null }) {
  return photoId ? <ProductThumb photoId={photoId} /> : <CategoryTile group={group} />;
}

/**
 * The row's name; with an AI-suggested name (Polish or Russian) a ✦ follows,
 * named for a screen reader like a suggested category. The ✦ is glued to the
 * last word so a narrow screen never wraps it onto a line of its own.
 */
function ProductTitle({ product }: { product: ProductSummary }) {
  const { t } = useTranslation();
  const name = displayName(product);
  if (!isNameSuggested(product)) return <>{name}</>;
  const split = name.lastIndexOf(" ");
  return (
    <>
      {split > 0 && `${name.slice(0, split)} `}
      <span className="pm-nowrap">
        {name.slice(split + 1)}
        {"\u00a0"}
        <span className="pm-product-meta-suggested" title={t("products.names.suggestedHint")}>
          <Sparkles size={14} role="img" aria-label={t("products.names.suggested")} aria-hidden={false} />
        </span>
      </span>
    </>
  );
}

/**
 * «Кефир натуральный · Проверить · Шоколад и конфеты ✦ · Wedel · 1 покупка»:
 * the Russian name first (under the Polish one), then only the category's
 * own (leaf) name, since the tile shows its group; a sparkle marks an AI
 * suggestion, named for a screen reader.
 */
function ProductMeta({ product, categories }: { product: ProductSummary; categories: ProductCategory[] }) {
  const { t } = useTranslation();
  const category = product.categoryId ? categories.find((entry) => entry.id === product.categoryId) : undefined;
  const parts: ReactNode[] = [];
  const russian = russianName(product);
  if (russian) parts.push(<span key="ru" lang="ru">{russian}</span>);
  if (product.needsReview) parts.push(<span key="review" className="pm-product-meta-review" title={t("products.needsReviewHint")}>{t("products.needsReviewLabel")}</span>);
  if (categories.length > 0) {
    if (category) {
      parts.push(
        <span key="category">
          {categoryLabel(t, category)}
          {product.categorySource === "suggested" && (
            <>
              {" "}
              <span className="pm-product-meta-suggested" title={t("categories.suggestedHint")}>
                <Sparkles size={14} role="img" aria-label={t("categories.suggested")} aria-hidden={false} />
              </span>
            </>
          )}
        </span>
      );
    } else {
      // No category, or one no longer in the tree (deleted meanwhile): the grey tile says the same.
      parts.push(<span key="category">{t("products.noCategory")}</span>);
    }
  }
  if (product.brand) parts.push(<span key="brand">{product.brand}</span>);
  parts.push(<span key="count" className="pm-nowrap">{t("products.purchaseCount", { count: product.purchaseCount })}</span>);
  return <>{parts.map((part, index) => <span key={index}>{index > 0 && " · "}{part}</span>)}</>;
}
