import { IonButton, IonLabel, IonList, IonSearchbar, IonSegment, IonSegmentButton } from "@ionic/react";
import { Camera, Receipt, SearchX } from "lucide-react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import type { BrandSummary, ProductCategory, PurchaseReviewFilter, PurchaseView, TagSummary } from "../../api/types";
import {
  ButtonLink,
  DateField,
  EmptyState,
  FilterButton,
  Footnote,
  LoadingList,
  Notice,
  Page,
  SectionHeader,
  Sheet
} from "../../design-system/components";
import { BrandFilterRow, CategoryFilterRow, TagFilterRow } from "../filters/FilterRows";
import { HelpLink } from "../help/HelpLink";
import { PurchaseRow } from "./PurchaseRow";
import { useReturnState } from "../../app/backNavigation";

export interface PurchaseFeedFilters {
  query: string;
  /** Inclusive local calendar day, `YYYY-MM-DD`, or an empty string. */
  from: string;
  /** Inclusive local calendar day, `YYYY-MM-DD`, or an empty string. */
  to: string;
  status: PurchaseReviewFilter;
  /** Category id, `"none"` for «Без категории», or `""` for any. */
  category: string;
  brand: string;
  /** Tag id, or `""` for any. */
  tag: string;
}

export const emptyPurchaseFeedFilters: PurchaseFeedFilters = {
  query: "",
  from: "",
  to: "",
  status: "all",
  category: "",
  brand: "",
  tag: ""
};

export function hasActiveFilters(filters: PurchaseFeedFilters): boolean {
  return filters.query.trim() !== "" || sheetFilterCount(filters) > 0;
}

/** Filters set in the sheet (the search is on screen), for the badge on «Фильтры». */
export function sheetFilterCount(filters: PurchaseFeedFilters): number {
  return [filters.from !== "" || filters.to !== "", filters.status !== "all", filters.category !== "", filters.brand.trim() !== "", filters.tag !== ""]
    .filter(Boolean).length;
}

const statusFilters: PurchaseReviewFilter[] = ["all", "needsAttention", "reviewed"];

export interface PurchaseFeedProps {
  filters: PurchaseFeedFilters;
  onFiltersChange: (filters: PurchaseFeedFilters) => void;
  /** The tree for the category filter; the category row hides while it is empty. */
  categories?: ProductCategory[];
  brands?: BrandSummary[];
  /** Household tags for the tag filter; the row hides while there are none. */
  tags?: TagSummary[];
  purchases: PurchaseView[];
  totalCount: number;
  loading?: boolean;
  error?: string | null;
  hasMore?: boolean;
  loadingMore?: boolean;
  onLoadMore?: () => void;
  /** The backend scanned its bounded candidate window and older rows were cut off. */
  truncated?: boolean;
  /** Opens with the filter sheet up; for stories. */
  initialFiltersOpen?: boolean;
}

/**
 * «Покупки»: a search under the large title, every other filter in a bottom
 * sheet behind the toolbar button (its badge counts what is set), and the
 * purchases as one grouped list.
 */
export function PurchaseFeed({
  filters,
  onFiltersChange,
  categories = [],
  brands = [],
  tags = [],
  purchases,
  totalCount,
  loading = false,
  error = null,
  hasMore = false,
  loadingMore = false,
  onLoadMore,
  truncated = false,
  initialFiltersOpen = false
}: PurchaseFeedProps) {
  const { t } = useTranslation();
  const returnState = useReturnState();
  const [filtersOpen, setFiltersOpen] = useState(initialFiltersOpen);
  const filtered = hasActiveFilters(filters);
  const update = (patch: Partial<PurchaseFeedFilters>) => onFiltersChange({ ...filters, ...patch });
  const resetAll = () => onFiltersChange(emptyPurchaseFeedFilters);

  return (
    <Page
      title={t("common.purchases")}
      end={<FilterButton count={sheetFilterCount(filters)} onClick={() => setFiltersOpen(true)} />}
      search={(
        <IonSearchbar
          value={filters.query}
          placeholder={t("feed.searchPlaceholder")}
          aria-label={t("feed.searchLabel")}
          debounce={0}
          onIonInput={(event) => update({ query: event.detail.value ?? "" })}
        />
      )}
    >
      <SectionHeader>
        {t("feed.title")}{!loading && !error ? ` · ${t("feed.count", { count: totalCount })}` : ""}
      </SectionHeader>

      {loading && <LoadingList label={t("feed.loading")} />}
      {!loading && error && <Notice tone="danger" role="alert"><p>{error}</p></Notice>}

      {!loading && !error && purchases.length === 0 && (
        filtered
          ? (
            <EmptyState
              icon={<SearchX />}
              title={t("feed.filteredEmptyTitle")}
              description={t("feed.filteredEmptyDescription")}
              action={<IonButton fill="clear" onClick={resetAll}>{t("feed.resetFilters")}</IonButton>}
            />
          )
          : (
            <EmptyState
              icon={<Receipt />}
              title={t("feed.emptyTitle")}
              description={t("feed.emptyDescription")}
              action={<ButtonLink to="/purchases/new" state={returnState} shape="round"><Camera aria-hidden="true" size={18} />&nbsp;{t("common.captureReceipt")}</ButtonLink>}
            />
          )
      )}

      {!loading && !error && purchases.length > 0 && (
        <>
          <IonList inset className="pm-after-header">
            {purchases.map((purchase) => <PurchaseRow key={purchase.id} purchase={purchase} />)}
          </IonList>
          {truncated && <Footnote>{t("feed.truncated")}</Footnote>}
          {hasMore && onLoadMore && (
            <div className="ion-padding-horizontal">
              <IonButton expand="block" fill="clear" onClick={onLoadMore} disabled={loadingMore}>
                {loadingMore ? t("feed.loadingMore") : t("feed.loadMore")}
              </IonButton>
            </div>
          )}
        </>
      )}

      <Footnote><HelpLink topic="feed" /></Footnote>

      <Sheet
        isOpen={filtersOpen}
        onClose={() => setFiltersOpen(false)}
        title={t("ui.filters")}
        start={<IonButton onClick={() => onFiltersChange({ ...emptyPurchaseFeedFilters, query: filters.query })}>{t("ui.reset")}</IonButton>}
      >
        <SectionHeader level={3}>{t("feed.periodLabel")}</SectionHeader>
        <IonList inset className="pm-after-header">
          <DateField label={t("feed.dateFrom")} value={filters.from} max={filters.to || undefined} onChange={(from) => update({ from })} />
          <DateField label={t("feed.dateTo")} value={filters.to} min={filters.from || undefined} onChange={(to) => update({ to })} />
        </IonList>

        <SectionHeader level={3}>{t("feed.statusLabel")}</SectionHeader>
        <IonList inset className="pm-after-header">
          <div className="ion-padding">
            <IonSegment value={filters.status} aria-label={t("feed.statusLabel")} onIonChange={(event) => update({ status: event.detail.value as PurchaseReviewFilter })}>
              {statusFilters.map((status) => (
                <IonSegmentButton key={status} value={status}><IonLabel>{t(`feed.status.${status}`)}</IonLabel></IonSegmentButton>
              ))}
            </IonSegment>
          </div>
        </IonList>

        <IonList inset>
          {categories.length > 0 && <CategoryFilterRow categories={categories} category={filters.category} onCategoryChange={(category) => update({ category })} />}
          <BrandFilterRow brands={brands} brand={filters.brand} onBrandChange={(brand) => update({ brand })} />
          {tags.length > 0 && <TagFilterRow tags={tags} tag={filters.tag} onTagChange={(tag) => update({ tag })} />}
        </IonList>
        <Footnote><HelpLink topic="filters" /></Footnote>
      </Sheet>
    </Page>
  );
}
