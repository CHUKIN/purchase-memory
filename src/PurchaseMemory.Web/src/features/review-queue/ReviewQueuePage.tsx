import { IonButton, IonList } from "@ionic/react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, Layers, SearchX } from "lucide-react";
import { useMemo, useRef, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useLocation, useNavigate } from "react-router";
import { classifyProduct, confirmProductNames, getReviewQueue, listCategories, listTags } from "../../api/client";
import type { ProductCategory, ProductSummary, ReviewQueue, ReviewQueueItem, ReviewQueueKind, TagSummary } from "../../api/types";
import {
  ActionButton,
  ButtonLink,
  EmptyState,
  FilterButton,
  Footnote,
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
import { tagsQueryKey } from "../tags/tagQueries";
import { useCreateProductForBarcode, useForgetBarcode, useLinkBarcode, useRememberedBarcodes, type RememberedBarcode } from "../product-scan/useProductMedia";
import { reviewQueueQueryKey } from "./reviewQueueQueries";
import { useBackTarget, useReturnState } from "../../app/backNavigation";
import { useToast } from "../../app/toasts";
import { HelpLink } from "../help/HelpLink";
import { BarcodeItems, type BarcodeItemsProps } from "./BarcodeSection";
import { ItemCard, shownNames } from "./ReviewItemCards";
import { ReviewCards } from "./ReviewCards";
import { cardSections, isCardSection, reviewSectionOrder, sectionKinds, type ReviewSectionId } from "./reviewSections";

export const reviewQueueLimitPerKind = 20;

/** Items a section shows before «Показать все». */
export const collapsedItemCount = 3;

/** `open`: the sections shown in full, comma-separated; in the URL so «Назад» finds them open. */
const emptyFilters = { category: "", tag: "", open: "" };

type Translate = ReturnType<typeof useTranslation>["t"];

/**
 * «Ревью» (`/needs-attention`): everything to check or do, one section per
 * kind of work, most important first, only sections with something in them.
 * `?cards=<section>` is the same section card by card («Проверять по одной»),
 * a history step of its own. The category and tag filters live in the URL
 * like in the feed and catalog; the Home card and the tab badge keep counting
 * the whole hub (useReviewCount).
 */
export function ReviewQueuePage() {
  const location = useLocation();
  const cards = new URLSearchParams(location.search).get("cards");
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
    enabled: !waitsForCategories && !waitsForTags && !isCardSection(cards)
  });
  const remembered = useRememberedBarcodes();
  const linkBarcode = useLinkBarcode();
  const forgetBarcode = useForgetBarcode();
  const createForBarcode = useCreateProductForBarcode();
  const navigate = useNavigate();
  const returnState = useReturnState();

  if (isCardSection(cards)) return <ReviewCards key={cards} section={cards} category={category} tag={tag} />;

  const open = filters.open ? filters.open.split(",") : [];
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
      openSections={open}
      onOpenSection={(section) => setFilters({ open: [...open.filter((entry) => entry !== section), section].join(",") })}
      barcodes={remembered.data ?? []}
      onLinkBarcode={async (code, product) => (await linkBarcode(product.id, code, "scan")).ok}
      loadSuggested={async (bulk) => suggestedOf(await getReviewQueue(bulkLimitPerKind, { category: category || undefined, tag: tag || undefined }), bulk)}
      onForgetBarcode={forgetBarcode}
      onCreateForBarcode={createForBarcode}
      onOpenProduct={(productId) => navigate(`/products/${productId}`, { state: returnState })}
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
  /** Sections shown in full (not just their first items). */
  openSections?: string[];
  onOpenSection?: (section: ReviewSectionId) => void;
  /** «Запомнить на потом» codes on this device. */
  barcodes?: RememberedBarcode[];
  onLinkBarcode?: (code: string, product: ProductSummary) => Promise<boolean>;
  onForgetBarcode?: (code: string) => Promise<void>;
  /** «Создать товар» for a remembered code; without it the button is not shown. */
  onCreateForBarcode?: BarcodeItemsProps["onCreate"];
  onOpenProduct?: (productId: string) => void;
  /**
   * Every suggestion of the kind «Принять все предложенные» confirms (up to
   * `bulkLimitPerKind`), read fresh with the screen's filters; by default the loaded ones.
   */
  loadSuggested?: (bulk: BulkAccept) => Promise<ReviewQueueItem[]>;
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
  openSections = [],
  onOpenSection = () => undefined,
  barcodes = [],
  onLinkBarcode = async () => false,
  onForgetBarcode = async () => undefined,
  onCreateForBarcode,
  onOpenProduct,
  loadSuggested,
  initialFiltersOpen = false
}: ReviewQueueViewProps) {
  const { t } = useTranslation();
  const [filtersOpen, setFiltersOpen] = useState(initialFiltersOpen);
  const filtered = category !== "" || tag !== "";
  const filterText = describeFilter(t, categories, category, tags, tag);
  const canFilter = categories.length > 0 || tags.length > 0;
  const resetButton = <IonButton fill="clear" onClick={onResetFilters}>{t("reviewQueue.resetFilter")}</IonButton>;
  const back = useBackTarget({ to: "/", labelKey: "common.home" });
  const filterKey = `${category}|${tag}`;
  const accept = useAcceptSuggested(filterKey);
  // Remembered codes have no product, so a product filter leaves them out (like unsaved receipts).
  const shownBarcodes = filtered ? [] : barcodes;
  const total = (queue?.totalCount ?? 0) - countHidden(queue?.groups.map((group) => group.kind) ?? [], queue, accept.hiddenBy, filterKey) + shownBarcodes.length;

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

      {!loading && queue && total <= 0 && (
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

      {!loading && queue && total > 0 && (
        <>
          {/* Each section shows its own count; the total is only said when a filter narrows it. */}
          <p className={filtered ? "pm-footnote" : "visually-hidden"} aria-live="polite">
            {filtered
              ? t("reviewQueue.filteredSummary", { count: total, filter: filterText })
              : t("reviewQueue.summary", { count: total })}
          </p>
          {reviewSectionOrder.map((section) => {
            if (section === "barcodes") {
              return shownBarcodes.length === 0 ? null : (
                <HubSection key={section} section={section} count={shownBarcodes.length}>
                  <BarcodeItems codes={shownBarcodes} onLink={onLinkBarcode} onForget={onForgetBarcode} onCreate={onCreateForBarcode} onOpenProduct={onOpenProduct} />
                </HubSection>
              );
            }
            const groups = sectionKinds[section]
              .map((kind) => queue.groups.find((group) => group.kind === kind))
              .filter((group) => group !== undefined);
            const items = groups.flatMap((group) => group.items).filter((item) => !accept.hidden.has(item.id));
            // Accepted categories leave at once, the ones not loaded on this screen too.
            const count = groups.reduce((sum, group) => sum + group.totalCount, 0) - countHidden(sectionKinds[section], queue, accept.hiddenBy, filterKey);
            if (count <= 0) return null;
            const isOpen = openSections.includes(section);
            const shown = isOpen ? items : items.slice(0, collapsedItemCount);
            const notLoaded = groups.reduce((sum, group) => sum + group.totalCount - group.items.length, 0);
            const bulk = section === "categories" || section === "names" ? bulkAccepts[section] : undefined;
            const suggestedGroup = bulk ? groups.find((group) => group.kind === bulk.kind) : undefined;
            // What the bulk accepts: every suggested category, up to the server's maximum per request.
            const suggestedCount = Math.min((suggestedGroup?.totalCount ?? 0) - countHidden(sectionKinds[section], queue, accept.hiddenBy, filterKey), bulkLimitPerKind);
            return (
              <HubSection key={section} section={section} count={count}>
                {(cardSections as readonly string[]).includes(section) && <CardsButton section={section} />}
                {suggestedCount > 1 && (
                  <div className="pm-review-bulk">
                    <ActionButton
                      fill="outline"
                      size="small"
                      loading={accept.loading}
                      disabled={accept.busy}
                      onClick={() => accept.run(bulk!, loadSuggested ? () => loadSuggested(bulk!) : async () => suggestedOf(queue, bulk!))}
                    >
                      {suggestedGroup!.totalCount > bulkLimitPerKind
                        ? t("reviewQueue.acceptFirstSuggested", { count: suggestedCount })
                        : t("reviewQueue.acceptAllSuggested", { count: suggestedCount })}
                    </ActionButton>
                  </div>
                )}
                {shown.map((item) => <ItemCard key={item.id} item={item} categories={categories} showKind={sectionKinds[section].length > 1} />)}
                {!isOpen && items.length > shown.length && (
                  <div className="ion-padding-horizontal">
                    <IonButton fill="clear" onClick={() => onOpenSection(section)}>{t("reviewQueue.showAll", { count: items.length })}</IonButton>
                  </div>
                )}
                {(isOpen || items.length <= collapsedItemCount) && notLoaded > 0 && <Footnote>{t("reviewQueue.more", { count: notLoaded })}</Footnote>}
              </HubSection>
            );
          })}
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

/**
 * Just accepted items still counted by the server, of the given kinds (an
 * item's id starts with its kind). A filter change must not subtract items
 * the new list never counted: an item counts if the loaded list shows it, or
 * if it was accepted under the very same filter (so it may sit beyond the
 * loaded window).
 */
function countHidden(kinds: readonly ReviewQueueKind[], queue: ReviewQueue | undefined, hiddenBy: ReadonlyMap<string, string>, filterKey: string) {
  const loaded = new Set(
    queue?.groups.filter((group) => kinds.includes(group.kind)).flatMap((group) => group.items.map((item) => item.id)) ?? []
  );
  let count = 0;
  for (const [id, key] of hiddenBy) {
    if (kinds.some((kind) => id.startsWith(`${kind}:`)) && (loaded.has(id) || key === filterKey)) count += 1;
  }

  return count;
}

/** «Принять все предложенные» reads this many per kind: the server's maximum. */
export const bulkLimitPerKind = 100;

/**
 * What «Принять все предложенные» does in a section: which kind it accepts,
 * how one item is confirmed and how the toast speaks of it.
 */
export interface BulkAccept {
  kind: ReviewQueueKind;
  ready: (item: ReviewQueueItem) => boolean;
  commit: (item: ReviewQueueItem) => Promise<unknown>;
  acceptedKey: string;
  failedKey: string;
  lateKey: string;
}

const bulkAccepts: Record<"categories" | "names", BulkAccept> = {
  categories: {
    kind: "suggestedCategory",
    ready: (item) => Boolean(item.productId && item.categoryId),
    commit: (item) => classifyProduct(item.productId!, { categoryId: item.categoryId }),
    acceptedKey: "reviewQueue.acceptedSuggested",
    failedKey: "reviewQueue.acceptAllFailed",
    lateKey: "reviewQueue.alreadyAccepted"
  },
  names: {
    kind: "suggestedNames",
    ready: (item) => Boolean(item.productId),
    // The names on the item go along: a name re-suggested meanwhile is not confirmed unseen (409).
    commit: (item) => confirmProductNames(item.productId!, shownNames(item)),
    acceptedKey: "reviewQueue.acceptedNames",
    failedKey: "reviewQueue.acceptNamesFailed",
    lateKey: "reviewQueue.namesAlreadyAccepted"
  }
};

function suggestedOf(queue: ReviewQueue | undefined, bulk: BulkAccept): ReviewQueueItem[] {
  return queue?.groups.find((group) => group.kind === bulk.kind)?.items.filter(bulk.ready) ?? [];
}

/** A section of the hub: its title and count, then its items. */
function HubSection({ section, count, children }: { section: ReviewSectionId; count: number; children: ReactNode }) {
  const { t } = useTranslation();
  const headingId = `review-section-${section}`;
  return (
    <section aria-labelledby={headingId} className="pm-review-section">
      <SectionHeader
        id={headingId}
        variant="title"
        action={<span className="pm-count-badge" aria-label={t("reviewQueue.groupCount", { count })}>{count}</span>}
      >
        {t(`reviewQueue.sections.${section}.title`)}
      </SectionHeader>
      {children}
    </section>
  );
}

/** «Проверять по одной»: the section card by card, a history step over the hub. */
function CardsButton({ section }: { section: ReviewSectionId }) {
  const { t } = useTranslation();
  const location = useLocation();
  const returnState = useReturnState();
  const parameters = new URLSearchParams(location.search);
  parameters.delete("open");
  parameters.set("cards", section);
  return (
    <div className="pm-review-bulk">
      <ButtonLink to={`/needs-attention?${parameters}`} state={returnState} size="small" fill="clear">
        <Layers size={18} aria-hidden="true" />&nbsp;{t("reviewQueue.oneByOne")}
      </ButtonLink>
    </div>
  );
}

/**
 * «Принять все предложенные» (categories or names): the suggestions (all of
 * them, read fresh with the screen's filters) leave the hub at once and a
 * toast offers «Вернуть». Nothing is sent while «Вернуть» can still be
 * pressed (the server has no way to make a confirmed value «suggested»
 * again): each product's suggestion is confirmed once the toast is gone, or
 * as soon as the page is hidden or closed, whichever comes first. «Вернуть»
 * after that only says they are already accepted.
 */
function useAcceptSuggested(filterKey: string) {
  const { t } = useTranslation();
  const showToast = useToast();
  const queryClient = useQueryClient();
  // Hidden id -> the filter it was accepted under (see countHidden).
  const [hiddenBy, setHiddenBy] = useState<ReadonlyMap<string, string>>(() => new Map());
  const [loading, setLoading] = useState(false);
  // Synchronous mirrors of the state: a second click can land before a re-render.
  const hiddenRef = useRef(new Set<string>());
  const loadingRef = useRef(false);
  const pendingRef = useRef(0);
  const [pending, setPending] = useState(0);
  const hidden = useMemo(() => new Set(hiddenBy.keys()), [hiddenBy]);

  const accept = (bulk: BulkAccept, items: ReviewQueueItem[]) => {
    const ids = items.map((item) => item.id);
    const unhide = () => {
      ids.forEach((id) => hiddenRef.current.delete(id));
      setHiddenBy((current) => new Map([...current].filter(([id]) => !ids.includes(id))));
    };
    const settle = () => {
      pendingRef.current -= 1;
      setPending(pendingRef.current);
    };
    ids.forEach((id) => hiddenRef.current.add(id));
    setHiddenBy((current) => new Map([...current, ...ids.map((id) => [id, filterKey] as const)]));
    pendingRef.current += 1;
    setPending(pendingRef.current);
    let undone = false;
    let committed = false;
    const onPageHide = () => void commit();
    const onVisibility = () => { if (document.visibilityState === "hidden") void commit(); };
    const stopListening = () => {
      window.removeEventListener("pagehide", onPageHide);
      document.removeEventListener("visibilitychange", onVisibility);
    };
    const commit = async () => {
      if (undone || committed) return;
      committed = true;
      stopListening();
      const results = await Promise.allSettled(items.map((item) => bulk.commit(item)));
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: reviewQueueQueryKey }),
        queryClient.invalidateQueries({ queryKey: ["products"] }),
        queryClient.invalidateQueries({ queryKey: ["purchases"] })
      ]);
      unhide();
      settle();
      const failed = results.filter((result) => result.status === "rejected").length;
      if (failed > 0) showToast({ id: `accept-failed-${Date.now()}`, message: t(bulk.failedKey, { count: failed }), tone: "danger" });
    };
    window.addEventListener("pagehide", onPageHide);
    document.addEventListener("visibilitychange", onVisibility);
    showToast({
      id: `accept-${Date.now()}`,
      message: t(bulk.acceptedKey, { count: items.length }),
      tone: "success",
      action: {
        label: t("reviewQueue.undo"),
        run: () => {
          if (committed) {
            showToast({ id: `accept-late-${Date.now()}`, message: t(bulk.lateKey), tone: "neutral" });
            return;
          }
          undone = true;
          stopListening();
          unhide();
          settle();
        }
      },
      onDismiss: () => void commit()
    });
  };

  const run = async (bulk: BulkAccept, load: () => Promise<ReviewQueueItem[]>) => {
    // A second press while the first is loading or waiting for its toast would re-pick the same items.
    if (loadingRef.current || pendingRef.current > 0) return;
    loadingRef.current = true;
    setLoading(true);
    try {
      const items = (await load()).filter((item) => !hiddenRef.current.has(item.id));
      if (items.length > 0) accept(bulk, items);
    } catch {
      showToast({ id: `accept-load-failed-${Date.now()}`, message: t("reviewQueue.loadError"), tone: "danger" });
    } finally {
      loadingRef.current = false;
      setLoading(false);
    }
  };

  return { hidden, hiddenBy, loading, busy: loading || pending > 0, run };
}

/** «из категории «Молоко» с тегом «Для ребёнка»»: the words after the count in the summary. */
function describeFilter(t: Translate, categories: ProductCategory[], category: string, tags: TagSummary[], tag: string) {
  const parts: string[] = [];
  if (category === "none") parts.push(t("reviewQueue.filterUncategorized"));
  else if (category) parts.push(t("reviewQueue.filterByCategory", { category: categoryPath(t, categories, category) ?? "" }));
  if (tag) parts.push(t("reviewQueue.filterByTag", { tag: tags.find((entry) => entry.id === tag)?.name ?? "" }));
  return parts.join(" ");
}
