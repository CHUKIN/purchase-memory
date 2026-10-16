import { IonButton, IonItem, IonLabel, IonList } from "@ionic/react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, SearchX } from "lucide-react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { classifyProduct, getReviewQueue, listCategories, listTags } from "../../api/client";
import type {
  ProductCategory,
  ReviewQueue,
  ReviewQueueGroup,
  ReviewQueueItem,
  ReviewQueueKind,
  TagSummary
} from "../../api/types";
import {
  ActionButton,
  EmptyState,
  FilterButton,
  Footnote,
  ItemLink,
  LoadingList,
  Notice,
  Page,
  SectionHeader,
  Sheet
} from "../../design-system/components";
import { categoryPath } from "../categories/categoryText";
import { categoriesQueryKey } from "../categories/categoryQueries";
import { CategoryFilterRow, TagFilterRow } from "../filters/FilterRows";
import { knownCategoryFilter, knownTagFilter } from "../filters/categoryFilter";
import { useUrlFilters } from "../filters/useUrlFilters";
import { formatMoney } from "../purchases/money";
import { purchaseEditPath } from "../purchases/PurchaseDetailPage";
import { tagsQueryKey } from "../tags/tagQueries";
import { reviewQueueQueryKey } from "./reviewQueueQueries";
import { useBackTarget, useReturnState } from "../../app/backNavigation";
import { MergeConfirmAlert, useConfirmDistinct, useMergeProduct, type MergeRequest } from "../products/productMerge";
import { HelpLink } from "../help/HelpLink";

export const reviewQueueLimitPerKind = 20;

const emptyFilters = { category: "", tag: "" };

type Translate = ReturnType<typeof useTranslation>["t"];

/**
 * Connects the queue to `GET /api/review-queue`. The category and tag filters
 * live in the URL (`category`, `tag`) like in the feed and catalog. The
 * badge on «Главная» keeps counting the whole queue; only this screen is narrowed.
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
  /** Opens with the filter sheet up; for stories. */
  initialFiltersOpen?: boolean;
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
  onResetFilters,
  initialFiltersOpen = false
}: ReviewQueueViewProps) {
  const { t } = useTranslation();
  const [filtersOpen, setFiltersOpen] = useState(initialFiltersOpen);
  const filtered = category !== "" || tag !== "";
  const filterText = describeFilter(t, categories, category, tags, tag);
  const canFilter = categories.length > 0 || tags.length > 0;
  const resetButton = <IonButton fill="clear" onClick={onResetFilters}>{t("reviewQueue.resetFilter")}</IonButton>;
  const back = useBackTarget({ to: "/", labelKey: "common.home" });

  return (
    <Page
      title={t("reviewQueue.title")}
      back={back}
      end={(
        <>
          <HelpLink topic="review-queue" />
          {canFilter && <FilterButton count={(category !== "" ? 1 : 0) + (tag !== "" ? 1 : 0)} onClick={() => setFiltersOpen(true)} />}
        </>
      )}
    >
      {filtered && <div className="ion-padding-horizontal">{resetButton}</div>}

      {loading && <LoadingList label={t("reviewQueue.loading")} rows={3} />}

      {!loading && error && (
        <Notice tone="danger" role="alert">
          <p>{t("reviewQueue.loadError")}</p>
          <IonButton fill="outline" size="small" onClick={onRetry}>{t("reviewQueue.retry")}</IonButton>
        </Notice>
      )}

      {!loading && queue && queue.totalCount === 0 && (
        filtered
          ? (
            // The one empty state that keeps a sentence: it names the filter that hides the rest.
            <EmptyState
              icon={<SearchX />}
              title={t("reviewQueue.filteredEmptyTitle")}
              description={t("reviewQueue.filteredEmptyDescription", { filter: filterText })}
              action={resetButton}
            />
          )
          : <EmptyState icon={<CheckCircle2 />} title={t("reviewQueue.emptyTitle")} />
      )}

      {!loading && queue && queue.totalCount > 0 && (
        <>
          {/* Each group shows its own count; the total is only said when a filter narrows it. */}
          <p className={filtered ? "pm-footnote" : "visually-hidden"} aria-live="polite">
            {filtered
              ? t("reviewQueue.filteredSummary", { count: queue.totalCount, filter: filterText })
              : t("reviewQueue.summary", { count: queue.totalCount })}
          </p>
          {queue.groups.map((group) => <GroupSection key={group.kind} group={group} categories={categories} />)}
        </>
      )}

      {canFilter && (
        <Sheet
          isOpen={filtersOpen}
          onClose={() => setFiltersOpen(false)}
          title={t("reviewQueue.filtersLabel")}
          start={<IonButton disabled={!filtered} onClick={onResetFilters}>{t("ui.reset")}</IonButton>}
          initialBreakpoint={0.5}
        >
          <IonList inset>
            {categories.length > 0 && <CategoryFilterRow categories={categories} category={category} onCategoryChange={onCategoryChange} />}
            {tags.length > 0 && <TagFilterRow tags={tags} tag={tag} onTagChange={onTagChange} />}
          </IonList>
        </Sheet>
      )}
    </Page>
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
    <section aria-labelledby={headingId}>
      <SectionHeader
        id={headingId}
        action={<span className="pm-count-badge" aria-label={t("reviewQueue.groupCount", { count: group.totalCount })}>{group.totalCount}</span>}
      >
        {t(`reviewQueue.kinds.${group.kind}.title`)}
      </SectionHeader>
      {group.items.map((item) => <ItemCard key={item.id} item={item} categories={categories} />)}
      {group.totalCount > group.items.length && <Footnote>{t("reviewQueue.more", { count: group.totalCount - group.items.length })}</Footnote>}
    </section>
  );
}

function ItemCard({ item, categories }: { item: ReviewQueueItem; categories: ProductCategory[] }) {
  const { t, i18n } = useTranslation();
  const title = item.title || t(`reviewQueue.untitled.${untitledKey(item.kind)}`);
  const date = new Intl.DateTimeFormat(i18n.language, { dateStyle: "medium" }).format(new Date(item.occurredAt));
  const flagged = item.kind === "productNeedsReview" && item.productId !== null;
  const why = reason(item, t, i18n.language);

  return (
    <article aria-label={title}>
      <IonList inset className="pm-after-header pm-queue-item">
        <IonItem>
          <IonLabel className="pm-wrap">
            <h3>{title}</h3>
            {why && <p>{why}</p>}
            <p><time dateTime={item.occurredAt}>{date}</time></p>
          </IonLabel>
        </IonItem>
        {flagged
          ? <FlaggedProductSuggestions item={item} title={title} categories={categories} />
          : <ItemActions item={item} title={title} />}
      </IonList>
    </article>
  );
}

/**
 * «Подтвердить» the AI's category, «Изменить» on the product's page. Used by
 * «Категория не подтверждена» and by a flagged product whose category is only suggested.
 */
function SuggestedCategoryActions({ item, title }: { item: ReviewQueueItem; title: string }) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const returnState = useReturnState();
  const confirm = useMutation({
    mutationFn: () => classifyProduct(item.productId!, { categoryId: item.categoryId }),
    onSuccess: () => Promise.all([
      queryClient.invalidateQueries({ queryKey: reviewQueueQueryKey }),
      queryClient.invalidateQueries({ queryKey: ["products"] })
    ])
  });
  const changeLabel = t("reviewQueue.changeCategory");
  return (
    <>
      <IonItem button detail={false} disabled={confirm.isPending} onClick={() => confirm.mutate()} aria-label={t("reviewQueue.confirmCategoryFor", { name: title, category: item.categoryName ?? "" })}>
        <IonLabel color="primary">{t("reviewQueue.confirmCategory")}</IonLabel>
      </IonItem>
      {item.productId && <ActionRow to={`/products/${item.productId}`} state={returnState} label={changeLabel} ariaLabel={`${changeLabel}: ${title}`} />}
      {confirm.isError && <IonItem lines="none"><IonLabel className="pm-wrap pm-danger-text" role="alert">{t("reviewQueue.confirmError")}</IonLabel></IonItem>}
    </>
  );
}

function ItemActions({ item, title }: { item: ReviewQueueItem; title: string }) {
  const { t } = useTranslation();
  // A screen opened from here comes back here (a receipt or purchase also after saving), with the filters as they were.
  const returnState = useReturnState();

  const receiptLink = item.receiptId
    ? <ActionRow key="receipt" to={`/receipts/${item.receiptId}`} state={returnState} label={t("reviewQueue.openReceipt")} />
    : item.purchaseId
      ? <ActionRow key="purchase" to={`/purchases/${item.purchaseId}`} state={returnState} label={t("reviewQueue.openPurchase")} />
      : null;
  // Saved purchases are fixed in the purchase editor, opened on the flagged spot.
  const editLink = (focus: string | undefined, label: string, labelFor: string) => item.purchaseId
    ? <ActionRow key="edit" to={purchaseEditPath(item.purchaseId, focus)} state={returnState} label={t(label)} ariaLabel={t(labelFor, { name: title })} />
    : null;
  const line = item.itemPosition ?? null;
  const productLink = (label: string) => item.productId
    ? <ActionRow key="product" to={`/products/${item.productId}`} state={returnState} label={label} ariaLabel={`${label}: ${title}`} />
    : null;

  switch (item.kind) {
    case "failedReceipt":
    case "unsavedReceipt":
      return receiptLink;
    case "totalMismatch":
      return <>{editLink("total", "reviewQueue.fixPurchase", "reviewQueue.fixPurchaseFor")}{item.receiptId && receiptLink}</>;
    case "missingPrice":
      return <>{editLink(line === null ? undefined : `price-${line}`, "reviewQueue.fixPurchase", "reviewQueue.fixPurchaseFor")}{item.receiptId && receiptLink}</>;
    case "productNeedsReview":
      return productLink(t("reviewQueue.openProduct"));
    case "suggestedCategory":
      return <SuggestedCategoryActions item={item} title={title} />;
    case "missingCategory":
      return productLink(t("reviewQueue.chooseCategory"));
  }
}

/** One fix as a full-width row in the item's group, the iOS way of listing actions. */
function ActionRow({ to, state, label, ariaLabel }: { to: string; state?: unknown; label: string; ariaLabel?: string }) {
  return (
    <ItemLink to={to} state={state} aria-label={ariaLabel}>
      <IonLabel color="primary">{label}</IonLabel>
    </ItemLink>
  );
}

/**
 * «Тот же товар или новый?» answered in place: the flagged product's
 * candidates with «Да, это он» (merge into it after a confirmation naming
 * both — there is no undo), and «Это другой товар». The queue, the badge and
 * the catalog refresh and the card disappears. A flagged product without a
 * category gets «Выбрать категорию» (its page) in place of «Открыть товар»;
 * one with only an AI-suggested category gets «Подтвердить» and «Изменить».
 */
function FlaggedProductSuggestions({ item, title, categories }: { item: ReviewQueueItem; title: string; categories: ProductCategory[] }) {
  const { t } = useTranslation();
  const merge = useMergeProduct();
  const distinct = useConfirmDistinct();
  const [request, setRequest] = useState<MergeRequest | null>(null);
  const [mergingInto, setMergingInto] = useState<string | null>(null);
  const candidates = item.candidates ?? [];
  const returnState = useReturnState();
  const busy = merge.isPending || distinct.isPending;
  const failed = merge.isError || distinct.isError;
  const productId = item.productId!;

  return (
    <>
      <div role="list" aria-label={t("reviewQueue.suggestionsFor", { name: title })}>
        {candidates.map((candidate, index) => {
          const details = [
            candidate.brand,
            categoryPath(t, categories, candidate.categoryId),
            candidate.purchaseCount !== undefined ? t("products.purchaseCount", { count: candidate.purchaseCount }) : null
          ].filter(Boolean).join(" · ");
          return (
            <IonItem key={candidate.productId} role="listitem">
              <IonLabel className="pm-wrap">
                {index === 0 && <p className="pm-overline" aria-hidden="true">{t("reviewQueue.suggestionsLabel")}</p>}
                <h3><Link className="pm-text-link" to={`/products/${candidate.productId}`} state={returnState}>{candidate.name}</Link></h3>
                {details && <p>{details}</p>}
              </IonLabel>
              <ActionButton
                slot="end"
                size="small"
                className="pm-tap-button"
                fill={index === 0 ? "solid" : "outline"}
                loading={merge.isPending && mergingInto === candidate.productId}
                disabled={busy && mergingInto !== candidate.productId}
                onClick={() => setRequest({ source: { id: productId, name: title }, target: { id: candidate.productId, name: candidate.name } })}
                aria-label={t("reviewQueue.acceptCandidateFor", { name: title, product: candidate.name })}
              >
                {t("reviewQueue.acceptCandidate")}
              </ActionButton>
            </IonItem>
          );
        })}
      </div>
      <IonItem
        button
        detail={false}
        disabled={busy}
        onClick={() => distinct.mutate(productId)}
        aria-label={t("reviewQueue.distinctProductFor", { name: title })}
      >
        <IonLabel color="primary">{t("reviewQueue.distinctProduct")}</IonLabel>
      </IonItem>
      {/* A new product's category step is on this card too, not a second item. */}
      {item.needsCategory
        ? <ActionRow to={`/products/${productId}`} state={returnState} label={t("reviewQueue.chooseCategory")} ariaLabel={`${t("reviewQueue.chooseCategory")}: ${title}`} />
        : item.categoryId
          ? <SuggestedCategoryActions item={item} title={title} />
          : <ActionRow to={`/products/${productId}`} state={returnState} label={t("reviewQueue.openProduct")} ariaLabel={`${t("reviewQueue.openProduct")}: ${title}`} />}
      {failed && (
        <IonItem lines="none">
          <IonLabel className="pm-wrap pm-danger-text" role="alert">{t("reviewQueue.resolveError")}</IonLabel>
        </IonItem>
      )}
      <MergeConfirmAlert
        request={request}
        onCancel={() => setRequest(null)}
        onConfirm={(confirmed) => { setMergingInto(confirmed.target.id); merge.mutate(confirmed); }}
      />
    </>
  );
}

function untitledKey(kind: ReviewQueueKind) {
  return kind === "suggestedCategory" || kind === "missingCategory" || kind === "productNeedsReview" ? "product" : "receipt";
}

/** Why the item is here, in a few words; null where the action below already says it. */
function reason(item: ReviewQueueItem, t: Translate, locale: string): string | null {
  const money = (value: number | null) => value === null ? "—" : formatMoney(value, item.currency, locale);

  switch (item.kind) {
    case "totalMismatch":
      return t("reviewQueue.kinds.totalMismatch.reason", { lines: money(item.linesTotal), total: money(item.receiptTotal) });
    case "missingPrice":
      return t("reviewQueue.kinds.missingPrice.reason", { count: item.affectedLineCount ?? 0 });
    case "suggestedCategory":
      return t("reviewQueue.kinds.suggestedCategory.reason", { category: item.categoryName ?? "—" });
    case "productNeedsReview":
      return [
        item.needsCategory
          ? t("reviewQueue.kinds.productNeedsReview.reasonWithCategory")
          : item.categoryId
            ? t("reviewQueue.kinds.productNeedsReview.reasonWithSuggestedCategory", { category: item.categoryName ?? "—" })
            : (item.candidates ?? []).length > 0
              ? t("reviewQueue.kinds.productNeedsReview.reason")
              : t("reviewQueue.kinds.productNeedsReview.reasonNoCandidates"),
        item.purchaseCount != null ? t("products.purchaseCount", { count: item.purchaseCount }) : null
      ].filter(Boolean).join(" ");
    case "missingCategory":
      return null;
    default:
      return t(`reviewQueue.kinds.${item.kind}.reason`);
  }
}
