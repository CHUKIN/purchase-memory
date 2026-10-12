import { Camera, Filter, Receipt, SearchX } from "lucide-react";
import { useId, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { BrandSummary, ProductCategory, PurchaseReviewFilter, PurchaseView, TagSummary } from "../../api/types";
import { DateInput, EmptyState, SkeletonCard, StatusBadge, Surface } from "../../design-system/components";
import { ClassificationFilterFields } from "../filters/ClassificationFilterFields";
import { issueCopy, issueKey } from "../issues/issueText";
import styles from "./PurchaseFeed.module.css";

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
  return filters.query.trim() !== ""
    || filters.from !== ""
    || filters.to !== ""
    || filters.status !== "all"
    || filters.category !== ""
    || filters.brand.trim() !== ""
    || filters.tag !== "";
}

const statusFilters: PurchaseReviewFilter[] = ["all", "needsAttention", "reviewed"];

export interface PurchaseFeedProps {
  filters: PurchaseFeedFilters;
  onFiltersChange: (filters: PurchaseFeedFilters) => void;
  /** The tree for the category filter; the category field hides while it is empty. */
  categories?: ProductCategory[];
  brands?: BrandSummary[];
  /** Household tags for the tag filter; the field hides while there are none. */
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
}

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
  truncated = false
}: PurchaseFeedProps) {
  const { t, i18n } = useTranslation();
  const groupId = useId();
  const locale = i18n.resolvedLanguage === "en" ? "en-GB" : "ru-RU";
  const filtered = hasActiveFilters(filters);

  // Cards already on screen should never replay their entrance animation;
  // only the ones a fresh `purchases` array actually introduces (e.g. from
  // "load more") get marked so their <li> can fade/slide in once.
  const [previousPurchases, setPreviousPurchases] = useState(purchases);
  const [newIds, setNewIds] = useState<ReadonlySet<string>>(new Set());
  if (purchases !== previousPurchases) {
    const previousIds = new Set(previousPurchases.map((purchase) => purchase.id));
    setNewIds(new Set(purchases.filter((purchase) => !previousIds.has(purchase.id)).map((purchase) => purchase.id)));
    setPreviousPurchases(purchases);
  }

  const update = (patch: Partial<PurchaseFeedFilters>) => onFiltersChange({ ...filters, ...patch });

  return (
    <section className={styles.feed} aria-labelledby={`${groupId}-title`}>
      <div className={styles.heading}>
        <h2 id={`${groupId}-title`}>{t("feed.title")}</h2>
        {!loading && !error && <p className={styles.count}>{t("feed.count", { count: totalCount })}</p>}
      </div>

      <Surface as="section" className={styles.filters} aria-label={t("feed.filtersLabel")}>
        <label className={styles.search}>
          <span className={styles.fieldLabel}>{t("feed.searchLabel")}</span>
          <input
            type="search"
            value={filters.query}
            placeholder={t("feed.searchPlaceholder")}
            onChange={(event) => update({ query: event.target.value })}
          />
        </label>

        <div className={styles.dates}>
          <label>
            <span className={styles.fieldLabel}>{t("feed.dateFrom")}</span>
            <DateInput value={filters.from} max={filters.to || undefined} onChange={(event) => update({ from: event.target.value })} />
          </label>
          <label>
            <span className={styles.fieldLabel}>{t("feed.dateTo")}</span>
            <DateInput value={filters.to} min={filters.from || undefined} onChange={(event) => update({ to: event.target.value })} />
          </label>
        </div>

        <fieldset className={styles.statusFilter}>
          <legend className={styles.fieldLabel}>{t("feed.statusLabel")}</legend>
          <div className={styles.segments}>
            {statusFilters.map((status) => (
              <label className={styles.segment} key={status} data-selected={filters.status === status}>
                <input
                  type="radio"
                  name={`${groupId}-status`}
                  value={status}
                  checked={filters.status === status}
                  onChange={() => update({ status })}
                />
                <span>{t(`feed.status.${status}`)}</span>
              </label>
            ))}
          </div>
        </fieldset>

        <div className={styles.classification}>
          <ClassificationFilterFields
            categories={categories}
            brands={brands}
            category={filters.category}
            onCategoryChange={(category) => update({ category })}
            brand={filters.brand}
            onBrandChange={(brand) => update({ brand })}
            tags={tags}
            tag={filters.tag}
            onTagChange={(tag) => update({ tag })}
          />
        </div>

        {filtered && (
          <button className={styles.reset} type="button" onClick={() => onFiltersChange(emptyPurchaseFeedFilters)}>
            {t("feed.resetFilters")}
          </button>
        )}
      </Surface>

      {loading && (
        <div aria-busy="true">
          <span className="visually-hidden">{t("feed.loading")}</span>
          <ul className={styles.list} aria-hidden="true">
            {Array.from({ length: 4 }, (_, index) => <li key={index}><SkeletonCard /></li>)}
          </ul>
        </div>
      )}
      {!loading && error && <Surface className={styles.notice} role="alert">{error}</Surface>}

      {!loading && !error && purchases.length === 0 && (
        filtered
          ? (
            <EmptyState
              icon={<SearchX />}
              title={t("feed.filteredEmptyTitle")}
              description={t("feed.filteredEmptyDescription")}
              action={<button className={styles.reset} type="button" onClick={() => onFiltersChange(emptyPurchaseFeedFilters)}>{t("feed.resetFilters")}</button>}
            />
          )
          : (
            <EmptyState
              icon={<Receipt />}
              title={t("feed.emptyTitle")}
              description={t("feed.emptyDescription")}
              action={<Link className={styles.emptyAction} to="/purchases/new"><Camera aria-hidden="true" />{t("common.captureReceipt")}</Link>}
            />
          )
      )}

      {!loading && !error && purchases.length > 0 && (
        <>
          <ul className={styles.list}>
            {purchases.map((purchase) => (
              <li key={purchase.id} className={newIds.has(purchase.id) ? styles.cardEnter : undefined}>
                <PurchaseFeedCard purchase={purchase} locale={locale} />
              </li>
            ))}
          </ul>

          {truncated && (
            <p className={styles.truncated}>
              <Filter aria-hidden="true" />
              {t("feed.truncated")}
            </p>
          )}

          {hasMore && onLoadMore && (
            <button className={styles.loadMore} type="button" onClick={onLoadMore} disabled={loadingMore}>
              {loadingMore ? t("feed.loadingMore") : t("feed.loadMore")}
            </button>
          )}
        </>
      )}
    </section>
  );
}

function PurchaseFeedCard({ purchase, locale }: { purchase: PurchaseView; locale: string }) {
  const { t } = useTranslation();
  const needsAttention = purchase.issues.length > 0;
  // Filled only while a category, brand or tag filter is on; the whole receipt
  // still opens, this just says which lines made it match.
  const matchedLines = purchase.items.filter((item) => item.matchesFilter === true);
  const date = purchase.purchasedAt
    ? new Intl.DateTimeFormat(locale, { dateStyle: "medium", timeStyle: "short" }).format(new Date(purchase.purchasedAt))
    : t("feed.unknownDate");
  const currency = purchase.currency ?? "PLN";
  const total = purchase.receiptTotal === null
    ? "—"
    : new Intl.NumberFormat(locale, { style: "currency", currency }).format(purchase.receiptTotal);

  return (
    <Surface as="article" className={styles.card}>
      <div className={styles.cardMain}>
        <h3 title={purchase.merchantName ?? undefined}><Link className={styles.cardLink} to={`/purchases/${purchase.id}`}>{purchase.merchantName ?? t("feed.unknownStore")}</Link></h3>
        <p className={styles.meta}>
          <span>{date}</span>
          <span aria-hidden="true">·</span>
          {/* Discounts are lines on the receipt, not items the user bought. */}
          <span>{t("feed.itemCount", { count: purchase.items.filter((item) => item.kind !== "discount").length })}</span>
        </p>
      </div>
      <div className={styles.cardSide}>
        <b className={styles.total}>{total}</b>
        <StatusBadge status={needsAttention ? "needsAttention" : "verified"} />
      </div>
      {needsAttention && (
        <ul className={styles.warnings}>
          {purchase.issues.map((issue) => <li key={issueKey(issue)}>{issueCopy(t, issue).title}</li>)}
        </ul>
      )}
      {matchedLines.length > 0 && (
        <p className={styles.matches}>
          <span className={styles.matchesLabel}>{t("filters.matchedLines", { count: matchedLines.length })}</span>{" "}
          {matchedLines.map((item) => item.name).join(", ")}
        </p>
      )}
    </Surface>
  );
}
