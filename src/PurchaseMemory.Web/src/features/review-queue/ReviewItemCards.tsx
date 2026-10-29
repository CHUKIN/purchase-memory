import { IonItem, IonLabel, IonList } from "@ionic/react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { classifyProduct, createImpression } from "../../api/client";
import type { ImpressionRating, ProductCategory, ReviewQueueItem, ReviewQueueKind } from "../../api/types";
import { ActionButton, ItemLink } from "../../design-system/components";
import { useReturnState, type ReturnState } from "../../app/backNavigation";
import { categoryPath } from "../categories/categoryText";
import { impressionsQueryKey } from "../impressions/impressionText";
import { RatingControl } from "../impressions/RatingControl";
import { formatShortDate } from "../purchases/dates";
import { formatMoney } from "../purchases/money";
import { purchaseEditPath } from "../purchases/PurchaseDetailPage";
import { openLineReviewKey } from "../purchases/openLineReview";
import { MergeConfirmAlert, useConfirmDistinct, useMergeProduct, type MergeRequest } from "../products/productMerge";
import { reviewQueueQueryKey } from "./reviewQueueQueries";

type Translate = ReturnType<typeof useTranslation>["t"];

/**
 * One item of the «Ревью» hub: what it is about, why it is here, and the
 * fix — in place where it is one tap (confirm a category, merge, rate), as a
 * link otherwise. Every link carries the return state, so «Назад» lands back
 * on the same spot of the hub. `showKind` names the kind above the title,
 * for sections that mix kinds (receipts, categories).
 */
export function ItemCard({ item, categories, showKind = false }: { item: ReviewQueueItem; categories: ProductCategory[]; showKind?: boolean }) {
  const { t, i18n } = useTranslation();
  const title = itemTitle(t, item);
  const date = new Intl.DateTimeFormat(i18n.language, { dateStyle: "medium" }).format(new Date(item.occurredAt));
  const flagged = item.kind === "productNeedsReview" && item.productId !== null;
  const why = reason(item, t, i18n.language);

  return (
    <article aria-label={title}>
      <IonList inset className="pm-after-header pm-queue-item">
        <IonItem>
          <IonLabel className="pm-wrap">
            {showKind && <p className="pm-overline">{t(`reviewQueue.kinds.${item.kind}.title`)}</p>}
            <h3>{title}</h3>
            {why && <p>{why}</p>}
            {item.kind !== "unratedPurchase" && <p><time dateTime={item.occurredAt}>{date}</time></p>}
          </IonLabel>
        </IonItem>
        {flagged
          ? <FlaggedProductSuggestions item={item} title={title} categories={categories} />
          : <ItemActions item={item} title={title} />}
      </IonList>
    </article>
  );
}

export function itemTitle(t: Translate, item: ReviewQueueItem): string {
  return item.title || t(`reviewQueue.untitled.${untitledKey(item.kind)}`);
}

/** Router state for opening a purchase straight into its line review, coming back to `returnState`. */
export function lineReviewState(returnState: ReturnState) {
  return { ...returnState, [openLineReviewKey]: true };
}

/** Confirms the AI's category: the product's category becomes the person's. */
export function useConfirmCategory() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ productId, categoryId }: { productId: string; categoryId: string | null }) => classifyProduct(productId, { categoryId }),
    onSuccess: () => Promise.all([
      queryClient.invalidateQueries({ queryKey: reviewQueueQueryKey }),
      queryClient.invalidateQueries({ queryKey: ["products"] })
    ])
  });
}

/** One tap rating of a line («Оценить»); the hub and every impression list refresh. */
export function useRateLine() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ purchaseItemId, rating }: { purchaseItemId: string; rating: ImpressionRating }) => createImpression({ purchaseItemId, rating }),
    onSuccess: () => Promise.all([
      queryClient.invalidateQueries({ queryKey: impressionsQueryKey }),
      queryClient.invalidateQueries({ queryKey: reviewQueueQueryKey })
    ])
  });
}

/**
 * «Подтвердить» the AI's category, «Изменить» on the product's page. Used by
 * «Категория не подтверждена» and by a flagged product whose category is only suggested.
 */
function SuggestedCategoryActions({ item, title }: { item: ReviewQueueItem; title: string }) {
  const { t } = useTranslation();
  const returnState = useReturnState();
  const confirm = useConfirmCategory();
  const changeLabel = t("reviewQueue.changeCategory");
  return (
    <>
      <IonItem button detail={false} disabled={confirm.isPending} onClick={() => confirm.mutate({ productId: item.productId!, categoryId: item.categoryId })} aria-label={t("reviewQueue.confirmCategoryFor", { name: title, category: item.categoryName ?? "" })}>
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
    case "unapprovedLines":
      return item.purchaseId
        ? <ActionRow to={`/purchases/${item.purchaseId}`} state={lineReviewState(returnState)} label={t("reviewQueue.checkLines")} ariaLabel={t("reviewQueue.checkLinesFor", { name: title })} />
        : null;
    case "unratedPurchase":
      return <RateActions item={item} title={title} />;
  }
}

/** The three-level rating in place; the item leaves the hub once rated. */
function RateActions({ item, title }: { item: ReviewQueueItem; title: string }) {
  const { t } = useTranslation();
  const returnState = useReturnState();
  const rate = useRateLine();
  return (
    <>
      {item.purchaseItemId && (
        <IonItem>
          <IonLabel className="pm-wrap">
            <RatingControl
              legend={t("impressions.quickLegend", { name: title })}
              hideLegend
              value={null}
              disabled={rate.isPending}
              onChange={(rating) => rate.mutate({ purchaseItemId: item.purchaseItemId!, rating })}
            />
            {rate.isError && <p className="pm-danger-text" role="alert">{t("impressions.saveFailed")}</p>}
          </IonLabel>
        </IonItem>
      )}
      {item.productId
        ? <ActionRow to={`/products/${item.productId}`} state={returnState} label={t("reviewQueue.openProduct")} ariaLabel={`${t("reviewQueue.openProduct")}: ${title}`} />
        : item.purchaseId && <ActionRow to={`/purchases/${item.purchaseId}`} state={returnState} label={t("reviewQueue.openPurchase")} ariaLabel={`${t("reviewQueue.openPurchase")}: ${title}`} />}
    </>
  );
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
 * `onResolved` runs after a merge or «Это другой товар» was saved (the card mode moves on).
 */
export function FlaggedProductSuggestions({ item, title, categories, onResolved, showCategoryStep = true }: {
  item: ReviewQueueItem;
  title: string;
  categories: ProductCategory[];
  onResolved?: () => void;
  showCategoryStep?: boolean;
}) {
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
        onClick={() => distinct.mutate(productId, { onSuccess: () => onResolved?.() })}
        aria-label={t("reviewQueue.distinctProductFor", { name: title })}
      >
        <IonLabel color="primary">{t("reviewQueue.distinctProduct")}</IonLabel>
      </IonItem>
      {/* A new product's category step is on this card too, not a second item. */}
      {showCategoryStep && (item.needsCategory
        ? <ActionRow to={`/products/${productId}`} state={returnState} label={t("reviewQueue.chooseCategory")} ariaLabel={`${t("reviewQueue.chooseCategory")}: ${title}`} />
        : item.categoryId
          ? <SuggestedCategoryActions item={item} title={title} />
          : <ActionRow to={`/products/${productId}`} state={returnState} label={t("reviewQueue.openProduct")} ariaLabel={`${t("reviewQueue.openProduct")}: ${title}`} />)}
      {failed && (
        <IonItem lines="none">
          <IonLabel className="pm-wrap pm-danger-text" role="alert">{t("reviewQueue.resolveError")}</IonLabel>
        </IonItem>
      )}
      <MergeConfirmAlert
        request={request}
        onCancel={() => setRequest(null)}
        onConfirm={(confirmed) => {
          setMergingInto(confirmed.target.id);
          merge.mutate(confirmed, { onSuccess: () => onResolved?.() });
        }}
      />
    </>
  );
}

function untitledKey(kind: ReviewQueueKind) {
  return kind === "suggestedCategory" || kind === "missingCategory" || kind === "productNeedsReview" || kind === "unratedPurchase" ? "product" : "receipt";
}

/** Why the item is here, in a few words; null where the action below already says it. */
export function reason(item: ReviewQueueItem, t: Translate, locale: string): string | null {
  const money = (value: number | null | undefined) => value === null || value === undefined ? "—" : formatMoney(value, item.currency, locale);

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
    case "unapprovedLines": {
      const total = item.lineCount ?? 0;
      return t("reviewQueue.kinds.unapprovedLines.reason", { approved: Math.max(0, total - (item.affectedLineCount ?? 0)), total });
    }
    case "unratedPurchase":
      return [
        item.merchantName ?? t("impressions.unknownStore"),
        formatShortDate(item.occurredAt, locale),
        item.price === null || item.price === undefined ? null : money(item.price)
      ].filter(Boolean).join(" · ");
    default:
      return t(`reviewQueue.kinds.${item.kind}.reason`);
  }
}
