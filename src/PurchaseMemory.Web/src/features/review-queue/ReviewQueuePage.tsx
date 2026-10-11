import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, SearchX } from "lucide-react";
import { useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Link, useLocation } from "react-router";
import { classifyProduct, getReviewQueue, listCategories, listTags } from "../../api/client";
import type {
  ProductCategory,
  ReviewQueue,
  ReviewQueueCandidate,
  ReviewQueueGroup,
  ReviewQueueItem,
  ReviewQueueKind,
  TagSummary
} from "../../api/types";
import { Button, EmptyState, SkeletonCard, Surface } from "../../design-system/components";
import { categoryPath } from "../categories/categoryText";
import { categoriesQueryKey } from "../categories/categoryQueries";
import { CategoryFilterField, TagFilterField } from "../filters/ClassificationFilterFields";
import { knownCategoryFilter, knownTagFilter } from "../filters/categoryFilter";
import { useUrlFilters } from "../filters/useUrlFilters";
import { formatMoney } from "../purchases/money";
import { purchaseEditPath } from "../purchases/PurchaseDetailPage";
import { tagsQueryKey } from "../tags/tagQueries";
import { reviewQueueQueryKey } from "./reviewQueueQueries";
import { resolvePendingMatch, StalePendingMatchError, type PendingMatchChoice } from "./resolvePendingMatch";
import { reviewQueueReturnState } from "./returnToQueue";
import { HelpLink } from "../help/HelpLink";
import styles from "./ReviewQueuePage.module.css";

export const reviewQueueLimitPerKind = 20;

const emptyFilters = { category: "", tag: "" };

type Translate = ReturnType<typeof useTranslation>["t"];

/**
 * Connects the queue to `GET /api/review-queue`. The category and tag filters
 * live in the URL (`category`, `tag`) like in the feed and catalog. The
 * navigation badge keeps counting the whole queue; only this screen is narrowed.
 */
export function ReviewQueuePage() {
  const [filters, setFilters] = useUrlFilters(emptyFilters);
  const categoriesQuery = useQuery({ queryKey: categoriesQueryKey, queryFn: listCategories });
  const tagsQuery = useQuery({ queryKey: tagsQueryKey, queryFn: listTags });
  // Same rule as the feed: a deleted category or tag from a link falls back to
  // the whole queue, and without its list the link's value is not sent at all.
  const category = categoriesQuery.isError ? "" : knownCategoryFilter(filters.category, categoriesQuery.data);
  const tag = tagsQuery.isError ? "" : knownTagFilter(filters.tag, tagsQuery.data);
  const waitsForCategories = filters.category !== "" && filters.category !== "none" && categoriesQuery.isPending;
  const waitsForTags = filters.tag !== "" && tagsQuery.isPending;
  const query = useQuery({
    queryKey: [...reviewQueueQueryKey, "list", reviewQueueLimitPerKind, category, tag],
    queryFn: () => getReviewQueue(reviewQueueLimitPerKind, { category: category || undefined, tag: tag || undefined }),
    enabled: !waitsForCategories && !waitsForTags
  });

  return (
    <ReviewQueueView
      queue={query.data}
      loading={query.isPending}
      error={query.isError}
      onRetry={() => void query.refetch()}
      categories={categoriesQuery.data ?? []}
      category={category}
      onCategoryChange={(value) => setFilters({ category: value })}
      tags={tagsQuery.data ?? []}
      tag={tag}
      onTagChange={(value) => setFilters({ tag: value })}
      onResetFilters={() => setFilters({ category: "", tag: "" })}
    />
  );
}

export interface ReviewQueueViewProps {
  queue: ReviewQueue | undefined;
  loading: boolean;
  error: boolean;
  onRetry: () => void;
  /** The category tree; the category filter hides until it is loaded. */
  categories: ProductCategory[];
  /** Category id, `"none"` for «Без категории», or `""` for the whole queue. */
  category: string;
  onCategoryChange: (category: string) => void;
  /** Household tags; the tag filter hides while there are none. */
  tags: TagSummary[];
  /** Tag id, or `""` for the whole queue. */
  tag: string;
  onTagChange: (tag: string) => void;
  onResetFilters: () => void;
}

export function ReviewQueueView({
  queue,
  loading,
  error,
  onRetry,
  categories,
  category,
  onCategoryChange,
  tags,
  tag,
  onTagChange,
  onResetFilters
}: ReviewQueueViewProps) {
  const { t } = useTranslation();
  const filtered = category !== "" || tag !== "";
  const filterText = describeFilter(t, categories, category, tags, tag);
  const resetButton = (
    <button className={styles.reset} type="button" onClick={onResetFilters}>
      {t("reviewQueue.resetFilter")}
    </button>
  );

  return (
    <div className={styles.page}>
      <header className={styles.hero}>
        <p className={styles.eyebrow}>{t("reviewQueue.eyebrow")}</p>
        <h1>{t("reviewQueue.title")}</h1>
        <p className={styles.intro}>{t("reviewQueue.intro")}</p>
        <HelpLink topic="review-queue" />
      </header>

      {(categories.length > 0 || tags.length > 0) && (
        <Surface as="section" className={styles.filters} aria-label={t("reviewQueue.filtersLabel")}>
          <div className={styles.filterFields}>
            {categories.length > 0 && <CategoryFilterField categories={categories} category={category} onCategoryChange={onCategoryChange} />}
            {tags.length > 0 && <TagFilterField tags={tags} tag={tag} onTagChange={onTagChange} />}
          </div>
          {filtered && (
            <>
              <p className={styles.filterNote}>{t("reviewQueue.filterNote")}</p>
              {resetButton}
            </>
          )}
        </Surface>
      )}

      {loading && (
        <div className={styles.loading} role="status" aria-label={t("reviewQueue.loading")}>
          <SkeletonCard />
          <SkeletonCard />
        </div>
      )}

      {!loading && error && (
        <Surface className={styles.notice} role="alert">
          <p>{t("reviewQueue.loadError")}</p>
          <Button intent="secondary" onClick={onRetry}>{t("reviewQueue.retry")}</Button>
        </Surface>
      )}

      {!loading && queue && queue.totalCount === 0 && (
        <Surface>
          {filtered
            ? (
              <EmptyState
                icon={<SearchX />}
                title={t("reviewQueue.filteredEmptyTitle")}
                description={t("reviewQueue.filteredEmptyDescription", { filter: filterText })}
                action={resetButton}
                headingLevel={2}
              />
            )
            : (
              <EmptyState
                icon={<CheckCircle2 />}
                title={t("reviewQueue.emptyTitle")}
                description={t("reviewQueue.emptyDescription")}
                headingLevel={2}
              />
            )}
        </Surface>
      )}

      {!loading && queue && queue.totalCount > 0 && (
        <>
          <p className={styles.summary} aria-live="polite">
            {filtered
              ? t("reviewQueue.filteredSummary", { count: queue.totalCount, filter: filterText })
              : t("reviewQueue.summary", { count: queue.totalCount })}
          </p>
          {queue.groups.map((group) => <GroupSection key={group.kind} group={group} categories={categories} />)}
        </>
      )}
    </div>
  );
}

/** «из категории «Молоко» с тегом «Для ребёнка»»: the words after the count in the summary. */
function describeFilter(t: Translate, categories: ProductCategory[], category: string, tags: TagSummary[], tag: string) {
  const parts: string[] = [];
  if (category === "none") parts.push(t("reviewQueue.filterUncategorized"));
  else if (category) parts.push(t("reviewQueue.filterByCategory", { category: categoryPath(t, categories, category) ?? "" }));
  if (tag) parts.push(t("reviewQueue.filterByTag", { tag: tags.find((entry) => entry.id === tag)?.name ?? "" }));
  return parts.join(" ");
}

function GroupSection({ group, categories }: { group: ReviewQueueGroup; categories: ProductCategory[] }) {
  const { t } = useTranslation();
  const headingId = `review-group-${group.kind}`;
  return (
    <section className={styles.group} aria-labelledby={headingId}>
      <header className={styles.groupHeader}>
        <h2 id={headingId}>{t(`reviewQueue.kinds.${group.kind}.title`)}</h2>
        <span className={styles.count} aria-label={t("reviewQueue.groupCount", { count: group.totalCount })}>{group.totalCount}</span>
      </header>
      <p className={styles.groupHint}>{t(`reviewQueue.kinds.${group.kind}.hint`)}</p>
      <ul className={styles.list}>
        {group.items.map((item) => <li key={item.id}><ItemCard item={item} categories={categories} /></li>)}
      </ul>
      {group.totalCount > group.items.length && (
        <p className={styles.more}>{t("reviewQueue.more", { count: group.totalCount - group.items.length })}</p>
      )}
    </section>
  );
}

function ItemCard({ item, categories }: { item: ReviewQueueItem; categories: ProductCategory[] }) {
  const { t, i18n } = useTranslation();
  const title = item.title || t(`reviewQueue.untitled.${untitledKey(item.kind)}`);
  const date = new Intl.DateTimeFormat(i18n.language, { dateStyle: "medium" }).format(new Date(item.occurredAt));
  const suggestions = item.kind === "pendingMatch" && item.purchaseId && (item.candidates?.length ?? 0) > 0;

  return (
    <Surface as="article" density="compact" className={styles.item} aria-label={title}>
      <div className={styles.itemText}>
        <h3>{title}</h3>
        <p>{reason(item, t, i18n.language)}</p>
        <p className={styles.meta}><time dateTime={item.occurredAt}>{date}</time></p>
      </div>
      {suggestions
        // «Другой товар» goes under the suggestions, next to «Создать новый товар»: it is the fallback, not the first choice.
        ? <PendingMatchSuggestions item={item} title={title} categories={categories} otherProduct={<ItemActions item={item} title={title} />} />
        : (
          <div className={styles.actions}>
            <ItemActions item={item} title={title} />
          </div>
        )}
    </Surface>
  );
}

function ItemActions({ item, title }: { item: ReviewQueueItem; title: string }) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  // A receipt or purchase opened from here comes back here after saving, with the filters as they were.
  const returnState = reviewQueueReturnState(useLocation().search);
  const confirm = useMutation({
    mutationFn: () => classifyProduct(item.productId!, { categoryId: item.categoryId }),
    onSuccess: () => Promise.all([
      queryClient.invalidateQueries({ queryKey: reviewQueueQueryKey }),
      queryClient.invalidateQueries({ queryKey: ["products"] })
    ])
  });

  const receiptLink = item.receiptId
    ? <Link className={styles.link} to={`/receipts/${item.receiptId}`} state={returnState}>{t("reviewQueue.openReceipt")}</Link>
    : item.purchaseId
      ? <Link className={styles.link} to={`/purchases/${item.purchaseId}`} state={returnState}>{t("reviewQueue.openPurchase")}</Link>
      : null;
  // Saved purchases are fixed in the purchase editor, opened on the flagged spot.
  const editLink = (focus: string | undefined, label: string, labelFor: string, primary = true) => item.purchaseId
    ? <Link className={primary ? styles.primaryLink : styles.link} to={purchaseEditPath(item.purchaseId, focus)} state={returnState} aria-label={t(labelFor, { name: title })}>{t(label)}</Link>
    : null;
  const line = item.itemPosition ?? null;
  const productLink = (label: string) => item.productId
    ? <Link className={styles.link} to={`/products/${item.productId}`} aria-label={`${label}: ${title}`}>{label}</Link>
    : null;

  switch (item.kind) {
    case "failedReceipt":
    case "unsavedReceipt":
      return receiptLink;
    case "totalMismatch":
      return <>{editLink("total", "reviewQueue.fixPurchase", "reviewQueue.fixPurchaseFor")}{item.receiptId && receiptLink}</>;
    case "missingPrice":
      return <>{editLink(line === null ? undefined : `price-${line}`, "reviewQueue.fixPurchase", "reviewQueue.fixPurchaseFor")}{item.receiptId && receiptLink}</>;
    case "pendingMatch": {
      const focus = line === null ? undefined : `product-${line}`;
      // With suggestions inline, the editor's picker stays one tap away for «another product».
      return (item.candidates?.length ?? 0) > 0
        ? editLink(focus, "reviewQueue.otherProduct", "reviewQueue.otherProductFor", false)
        : <>{editLink(focus, "reviewQueue.chooseProduct", "reviewQueue.chooseProductFor")}{productLink(t("reviewQueue.openSimilarProduct"))}</>;
    }
    case "suggestedCategory":
      return (
        <>
          <Button
            intent="primary"
            loading={confirm.isPending}
            onClick={() => confirm.mutate()}
            aria-label={t("reviewQueue.confirmCategoryFor", { name: title, category: item.categoryName ?? "" })}
          >
            {t("reviewQueue.confirmCategory")}
          </Button>
          {productLink(t("reviewQueue.changeCategory"))}
          {confirm.isError && <p className={styles.error} role="alert">{t("reviewQueue.confirmError")}</p>}
        </>
      );
    case "missingCategory":
      return productLink(t("reviewQueue.chooseCategory"));
  }
}

/**
 * «Товар не сопоставлен» answered in place: the matcher's best guesses with a
 * one-tap «Да, это он», and «Создать новый товар». Both save the purchase
 * through the same API as the editor's picker, so the store remembers the
 * choice; the queue and the badge then refresh and the card disappears.
 */
function PendingMatchSuggestions({ item, title, categories, otherProduct }: { item: ReviewQueueItem; title: string; categories: ProductCategory[]; otherProduct: ReactNode }) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [choice, setChoice] = useState<PendingMatchChoice | null>(null);
  const refreshQueue = () => queryClient.invalidateQueries({ queryKey: reviewQueueQueryKey });
  const resolve = useMutation({
    mutationFn: (next: PendingMatchChoice) => resolvePendingMatch(item, next),
    onSuccess: () => Promise.all([
      refreshQueue(),
      queryClient.invalidateQueries({ queryKey: ["purchases"] }),
      queryClient.invalidateQueries({ queryKey: ["products"] }),
      item.receiptId ? queryClient.invalidateQueries({ queryKey: ["receipts", item.receiptId] }) : Promise.resolve()
    ]),
    // Someone already decided this line: the queue is out of date, not the person.
    onError: (error) => error instanceof StalePendingMatchError ? refreshQueue() : undefined
  });
  const choose = (next: PendingMatchChoice) => { setChoice(next); resolve.mutate(next); };
  const busy = (candidate: ReviewQueueCandidate | "new") => resolve.isPending && (candidate === "new"
    ? choice === "new"
    : choice !== null && choice !== "new" && choice.productId === candidate.productId);
  const candidates = item.candidates ?? [];

  return (
    <div className={styles.suggestions}>
      <p className={styles.suggestionsLabel} aria-hidden="true">{t("reviewQueue.suggestionsLabel")}</p>
      <ul className={styles.suggestionList} aria-label={t("reviewQueue.suggestionsFor", { name: title })}>
        {candidates.map((candidate, index) => {
          const details = [candidate.brand, categoryPath(t, categories, candidate.categoryId)].filter(Boolean).join(" · ");
          return (
            <li key={candidate.productId} className={styles.suggestion}>
              <div className={styles.suggestionText}>
                <Link className={styles.suggestionName} to={`/products/${candidate.productId}`}>{candidate.name}</Link>
                {details && <span className={`${styles.meta} ${styles.details}`} title={details}>{details}</span>}
              </div>
              <Button
                intent={index === 0 ? "primary" : "secondary"}
                loading={busy(candidate)}
                disabled={resolve.isPending && !busy(candidate)}
                onClick={() => choose({ productId: candidate.productId })}
                aria-label={t("reviewQueue.acceptCandidateFor", { name: title, product: candidate.name })}
              >
                {t("reviewQueue.acceptCandidate")}
              </Button>
            </li>
          );
        })}
      </ul>
      <div className={styles.suggestionActions}>
        <Button
          intent="quiet"
          loading={busy("new")}
          disabled={resolve.isPending && !busy("new")}
          onClick={() => choose("new")}
          aria-label={t("reviewQueue.createProductFor", { name: title })}
        >
          {t("reviewQueue.createProduct")}
        </Button>
        {otherProduct}
      </div>
      {resolve.isError && (
        <p className={styles.error} role="alert">
          {resolve.error instanceof StalePendingMatchError ? t("reviewQueue.resolveStale") : t("reviewQueue.resolveError")}
        </p>
      )}
    </div>
  );
}

function untitledKey(kind: ReviewQueueKind) {
  return kind === "suggestedCategory" || kind === "missingCategory" || kind === "pendingMatch" ? "product" : "receipt";
}

function reason(item: ReviewQueueItem, t: Translate, locale: string) {
  const money = (value: number | null) => value === null ? "—" : formatMoney(value, item.currency, locale);

  switch (item.kind) {
    case "totalMismatch":
      return t("reviewQueue.kinds.totalMismatch.reason", { lines: money(item.linesTotal), total: money(item.receiptTotal) });
    case "missingPrice":
      return t("reviewQueue.kinds.missingPrice.reason", { count: item.affectedLineCount ?? 0 });
    case "suggestedCategory":
      return t("reviewQueue.kinds.suggestedCategory.reason", { category: item.categoryName ?? "—" });
    case "pendingMatch":
      return (item.candidates?.length ?? 0) > 0 && item.purchaseId
        ? t("reviewQueue.kinds.pendingMatch.reasonWithCandidates")
        : t("reviewQueue.kinds.pendingMatch.reason");
    default:
      return t(`reviewQueue.kinds.${item.kind}.reason`);
  }
}
