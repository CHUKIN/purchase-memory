import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, SearchX } from "lucide-react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { classifyProduct, getReviewQueue, listTags } from "../../api/client";
import type { ReviewQueue, ReviewQueueGroup, ReviewQueueItem, ReviewQueueKind, TagSummary } from "../../api/types";
import { Button, EmptyState, SkeletonCard, Surface } from "../../design-system/components";
import { TagFilterField } from "../filters/ClassificationFilterFields";
import { knownTagFilter } from "../filters/categoryFilter";
import { useUrlFilters } from "../filters/useUrlFilters";
import { formatMoney } from "../purchases/money";
import { purchaseEditPath } from "../purchases/PurchaseDetailPage";
import { tagsQueryKey } from "../tags/tagQueries";
import { reviewQueueQueryKey } from "./reviewQueueQueries";
import { HelpLink } from "../help/HelpLink";
import styles from "./ReviewQueuePage.module.css";

export const reviewQueueLimitPerKind = 20;

const emptyFilters = { tag: "" };

/**
 * Connects the queue to `GET /api/review-queue`. The tag filter lives in the
 * URL (`tag`) like in the feed and catalog. The navigation badge keeps
 * counting the whole queue; only this screen is narrowed.
 */
export function ReviewQueuePage() {
  const [filters, setFilters] = useUrlFilters(emptyFilters);
  const tagsQuery = useQuery({ queryKey: tagsQueryKey, queryFn: listTags });
  // Same rule as the feed: a deleted tag from a link falls back to the whole
  // queue, and without the tag list the link's tag is not sent at all.
  const tag = tagsQuery.isError ? "" : knownTagFilter(filters.tag, tagsQuery.data);
  const query = useQuery({
    queryKey: [...reviewQueueQueryKey, "list", reviewQueueLimitPerKind, tag],
    queryFn: () => getReviewQueue(reviewQueueLimitPerKind, tag || undefined),
    enabled: filters.tag === "" || !tagsQuery.isPending
  });

  return (
    <ReviewQueueView
      queue={query.data}
      loading={query.isPending}
      error={query.isError}
      onRetry={() => void query.refetch()}
      tags={tagsQuery.data ?? []}
      tag={tag}
      onTagChange={(value) => setFilters({ tag: value })}
    />
  );
}

export interface ReviewQueueViewProps {
  queue: ReviewQueue | undefined;
  loading: boolean;
  error: boolean;
  onRetry: () => void;
  /** Household tags; the filter hides while there are none. */
  tags: TagSummary[];
  /** Tag id, or `""` for the whole queue. */
  tag: string;
  onTagChange: (tag: string) => void;
}

export function ReviewQueueView({ queue, loading, error, onRetry, tags, tag, onTagChange }: ReviewQueueViewProps) {
  const { t } = useTranslation();
  const tagName = tag ? tags.find((entry) => entry.id === tag)?.name ?? "" : "";
  const filtered = tag !== "";
  const resetButton = (
    <button className={styles.reset} type="button" onClick={() => onTagChange("")}>
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

      {tags.length > 0 && (
        <Surface as="section" className={styles.filters} aria-label={t("reviewQueue.filtersLabel")}>
          <TagFilterField tags={tags} tag={tag} onTagChange={onTagChange} />
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
                description={t("reviewQueue.filteredEmptyDescription", { tag: tagName })}
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
              ? t("reviewQueue.filteredSummary", { count: queue.totalCount, tag: tagName })
              : t("reviewQueue.summary", { count: queue.totalCount })}
          </p>
          {queue.groups.map((group) => <GroupSection key={group.kind} group={group} />)}
        </>
      )}
    </div>
  );
}

function GroupSection({ group }: { group: ReviewQueueGroup }) {
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
        {group.items.map((item) => <li key={item.id}><ItemCard item={item} /></li>)}
      </ul>
      {group.totalCount > group.items.length && (
        <p className={styles.more}>{t("reviewQueue.more", { count: group.totalCount - group.items.length })}</p>
      )}
    </section>
  );
}

function ItemCard({ item }: { item: ReviewQueueItem }) {
  const { t, i18n } = useTranslation();
  const title = item.title || t(`reviewQueue.untitled.${untitledKey(item.kind)}`);
  const date = new Intl.DateTimeFormat(i18n.language, { dateStyle: "medium" }).format(new Date(item.occurredAt));

  return (
    <Surface as="article" density="compact" className={styles.item} aria-label={title}>
      <div className={styles.itemText}>
        <h3>{title}</h3>
        <p>{reason(item, t, i18n.language)}</p>
        <p className={styles.meta}><time dateTime={item.occurredAt}>{date}</time></p>
      </div>
      <div className={styles.actions}>
        <ItemActions item={item} title={title} />
      </div>
    </Surface>
  );
}

function ItemActions({ item, title }: { item: ReviewQueueItem; title: string }) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const confirm = useMutation({
    mutationFn: () => classifyProduct(item.productId!, { categoryId: item.categoryId }),
    onSuccess: () => Promise.all([
      queryClient.invalidateQueries({ queryKey: reviewQueueQueryKey }),
      queryClient.invalidateQueries({ queryKey: ["products"] })
    ])
  });

  const receiptLink = item.receiptId
    ? <Link className={styles.link} to={`/receipts/${item.receiptId}`}>{t("reviewQueue.openReceipt")}</Link>
    : item.purchaseId
      ? <Link className={styles.link} to={`/purchases/${item.purchaseId}`}>{t("reviewQueue.openPurchase")}</Link>
      : null;
  // Saved purchases are fixed in the purchase editor, opened on the flagged spot.
  const editLink = (focus: string | undefined, label: string, labelFor: string) => item.purchaseId
    ? <Link className={styles.primaryLink} to={purchaseEditPath(item.purchaseId, focus)} aria-label={t(labelFor, { name: title })}>{t(label)}</Link>
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
    case "pendingMatch":
      return <>{editLink(line === null ? undefined : `product-${line}`, "reviewQueue.chooseProduct", "reviewQueue.chooseProductFor")}{productLink(t("reviewQueue.openSimilarProduct"))}</>;
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

function untitledKey(kind: ReviewQueueKind) {
  return kind === "suggestedCategory" || kind === "missingCategory" || kind === "pendingMatch" ? "product" : "receipt";
}

function reason(item: ReviewQueueItem, t: ReturnType<typeof useTranslation>["t"], locale: string) {
  const money = (value: number | null) => value === null ? "—" : formatMoney(value, item.currency, locale);

  switch (item.kind) {
    case "totalMismatch":
      return t("reviewQueue.kinds.totalMismatch.reason", { lines: money(item.linesTotal), total: money(item.receiptTotal) });
    case "missingPrice":
      return t("reviewQueue.kinds.missingPrice.reason", { count: item.affectedLineCount ?? 0 });
    case "suggestedCategory":
      return t("reviewQueue.kinds.suggestedCategory.reason", { category: item.categoryName ?? "—" });
    default:
      return t(`reviewQueue.kinds.${item.kind}.reason`);
  }
}
