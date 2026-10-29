import { IonButton, IonItem, IonLabel, IonList, IonProgressBar } from "@ionic/react";
import { useQuery } from "@tanstack/react-query";
import { CheckCircle2, ChevronLeft } from "lucide-react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router";
import { getReviewQueue, listCategories } from "../../api/client";
import type { ProductCategory, ReviewQueue, ReviewQueueItem } from "../../api/types";
import { useBackTarget, useReturnState } from "../../app/backNavigation";
import { useArrivalPlace, useRememberLine } from "../../app/place";
import { ActionButton, EmptyState, LoadingList, Notice, Page, useBack } from "../../design-system/components";
import { categoryPath } from "../categories/categoryText";
import { categoriesQueryKey } from "../categories/categoryQueries";
import { CategoryPicker } from "../categories/CategoryPicker";
import { RatingControl } from "../impressions/RatingControl";
import { useSwipe } from "../line-review/useSwipe";
import { FlaggedProductSuggestions, itemTitle, reason, useConfirmCategory, useConfirmNames, useRateLine } from "./ReviewItemCards";
import { reviewQueueQueryKey } from "./reviewQueueQueries";
import { sectionKinds, type CardSectionId } from "./reviewSections";

/** Card mode reads up to this many items of its section (the server's maximum per kind). */
export const cardModeLimitPerKind = 100;

export interface ReviewCardsProps {
  section: CardSectionId;
  /** The hub's filters, so the cards are the same items the hub shows. */
  category: string;
  tag: string;
}

/**
 * «Проверять по одной»: a section's items one card at a time, with «N из M»
 * on top. Like the line review, a swipe turns the page (to the left: the next
 * card, so it also skips; to the right: back) and the main button answers
 * («Верно, дальше» confirms a suggested category); «Пропустить» moves on
 * without answering. After the last card the screen steps back to the hub,
 * which is where it was opened from. Opening a product from a card remembers
 * the card, so «Назад» from the product comes back to it.
 */
export function ReviewCards({ section, category, tag }: ReviewCardsProps) {
  const query = useQuery({
    queryKey: [...reviewQueueQueryKey, "list", cardModeLimitPerKind, category, tag],
    queryFn: () => getReviewQueue(cardModeLimitPerKind, { category: category || undefined, tag: tag || undefined })
  });
  const categoriesQuery = useQuery({ queryKey: categoriesQueryKey, queryFn: listCategories, enabled: section !== "ratings" && section !== "names" });
  return (
    <ReviewCardsView
      section={section}
      queue={query.data}
      loading={query.isPending}
      error={query.isError}
      onRetry={() => void query.refetch()}
      categories={categoriesQuery.data ?? []}
    />
  );
}

export interface ReviewCardsViewProps {
  section: CardSectionId;
  queue: ReviewQueue | undefined;
  loading: boolean;
  error: boolean;
  onRetry: () => void;
  categories: ProductCategory[];
}

export function ReviewCardsView({ section, queue, loading, error, onRetry, categories }: ReviewCardsViewProps) {
  const { t } = useTranslation();
  const back = useBackTarget({ to: "/needs-attention", labelKey: "common.review" });
  const finish = useBack(back);
  const arrival = useArrivalPlace();
  // The items as they were when the cards opened: answered ones leave the
  // server's list, but the counter and «назад» keep their place.
  const [cards, setCards] = useState<ReviewQueueItem[] | null>(null);
  const [index, setIndex] = useState(0);
  const [done, setDone] = useState<Set<string>>(() => new Set());
  if (cards === null && queue) {
    const kinds = sectionKinds[section];
    const items = kinds.flatMap((kind) => queue.groups.find((group) => group.kind === kind)?.items ?? []);
    setCards(items);
    setIndex(Math.min(arrival?.line ?? 0, Math.max(0, items.length - 1)));
  }

  const count = cards?.length ?? 0;
  const go = (next: number) => {
    if (next < 0) return;
    if (next >= count) finish();
    else setIndex(next);
  };
  const swipe = useSwipe((step) => go(index + step), false);
  const card = cards?.[index];
  const resolve = () => {
    if (!card) return;
    setDone((current) => new Set(current).add(card.id));
    go(index + 1);
  };
  // Where this card will be in the list the server sends back later: the answered ones before it are gone by then.
  const placeOnReturn = cards ? cards.slice(0, index).filter((item) => !done.has(item.id)).length : 0;

  const title = t(`reviewQueue.sections.${section}.title`);
  return (
    <Page title={title} back={back} largeTitle={false}>
      {loading && <LoadingList label={t("reviewQueue.loading")} rows={2} />}
      {!loading && error && (
        <Notice tone="danger" role="alert">
          <p>{t("reviewQueue.loadError")}</p>
          <IonButton fill="outline" size="small" onClick={onRetry}>{t("reviewQueue.retry")}</IonButton>
        </Notice>
      )}
      {cards && count === 0 && <EmptyState icon={<CheckCircle2 />} title={t("reviewQueue.cards.empty")} />}
      {card && (
        <div className="pm-review-cards">
          <p className="pm-review-cards-progress" aria-live="polite">{t("reviewQueue.cards.progress", { current: index + 1, total: count })}</p>
          <IonProgressBar className="pm-review-cards-bar" value={(index + 1) / count} aria-hidden="true" />
          <div className="pm-review-cards-card" key={card.id} data-testid="review-card" {...swipe.handlers}>
            {done.has(card.id)
              ? <DoneCard item={card} />
              : <CardBody item={card} section={section} categories={categories} onResolved={resolve} placeOnReturn={placeOnReturn} />}
          </div>
          <div className="pm-review-cards-nav">
            <IonButton fill="clear" disabled={index === 0} onClick={() => go(index - 1)} aria-label={t("reviewQueue.cards.previous")}>
              <ChevronLeft size={20} aria-hidden="true" />
            </IonButton>
            <IonButton fill="clear" onClick={() => go(index + 1)}>
              {done.has(card.id) ? t("reviewQueue.cards.next") : t("reviewQueue.cards.skip")}
            </IonButton>
          </div>
        </div>
      )}
    </Page>
  );
}

function DoneCard({ item }: { item: ReviewQueueItem }) {
  const { t } = useTranslation();
  return (
    <IonList inset className="pm-queue-item">
      <IonItem lines="none">
        <CheckCircle2 slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
        <IonLabel className="pm-wrap">
          <h3>{itemTitle(t, item)}</h3>
          <p>{t("reviewQueue.cards.answered")}</p>
        </IonLabel>
      </IonItem>
    </IonList>
  );
}

function CardBody({ item, section, categories, onResolved, placeOnReturn }: {
  item: ReviewQueueItem;
  section: CardSectionId;
  categories: ProductCategory[];
  onResolved: () => void;
  placeOnReturn: number;
}) {
  const { t, i18n } = useTranslation();
  const title = itemTitle(t, item);
  const why = reason(item, t, i18n.language);
  return (
    <IonList inset className="pm-queue-item">
      <IonItem>
        <IonLabel className="pm-wrap">
          <p className="pm-overline">{t(`reviewQueue.kinds.${item.kind}.title`)}</p>
          <h2 className="pm-review-cards-title">{title}</h2>
          {why && <p>{why}</p>}
        </IonLabel>
      </IonItem>
      {section === "products" && item.productId && (
        <FlaggedProductSuggestions item={item} title={title} categories={categories} onResolved={onResolved} showCategoryStep={false} />
      )}
      {section === "names" && <NamesCard item={item} title={title} onResolved={onResolved} />}
      {section === "categories" && <CategoryCard item={item} title={title} categories={categories} onResolved={onResolved} />}
      {section === "ratings" && <RatingCard item={item} title={title} onResolved={onResolved} />}
      <OpenProductRow item={item} title={title} placeOnReturn={placeOnReturn} />
    </IonList>
  );
}

/** A suggested category: «Верно, дальше» confirms it; any card can pick another (or a first) category. */
function CategoryCard({ item, title, categories, onResolved }: { item: ReviewQueueItem; title: string; categories: ProductCategory[]; onResolved: () => void }) {
  const { t } = useTranslation();
  const confirm = useConfirmCategory();
  const save = (categoryId: string | null) => {
    if (!item.productId) return;
    confirm.mutate({ productId: item.productId, categoryId }, { onSuccess: onResolved });
  };
  const suggested = item.kind === "suggestedCategory" && item.categoryId !== null;
  return (
    <>
      {suggested && (
        // A full-width row of its own: inside an IonItem the button would shrink to the item's small size.
        <div className="pm-review-cards-primary-row">
          <ActionButton
            expand="block"
            className="pm-review-cards-primary"
            loading={confirm.isPending}
            onClick={() => save(item.categoryId)}
            aria-label={t("reviewQueue.confirmCategoryFor", { name: title, category: categoryPath(t, categories, item.categoryId) ?? item.categoryName ?? "" })}
          >
            {t("reviewQueue.cards.correct")}
          </ActionButton>
        </div>
      )}
      {categories.length > 0 && (
        <CategoryPicker
          categories={categories}
          value={suggested ? item.categoryId : null}
          label={suggested ? t("reviewQueue.cards.otherCategory") : t("reviewQueue.chooseCategory")}
          disabled={confirm.isPending}
          onChange={save}
        />
      )}
      {confirm.isError && <IonItem lines="none"><IonLabel className="pm-wrap pm-danger-text" role="alert">{t("reviewQueue.confirmError")}</IonLabel></IonItem>}
    </>
  );
}

/** Suggested names: «Верно, дальше» confirms them; editing happens on the product («Открыть товар»). */
function NamesCard({ item, title, onResolved }: { item: ReviewQueueItem; title: string; onResolved: () => void }) {
  const { t } = useTranslation();
  const confirm = useConfirmNames();
  if (!item.productId) return null;
  return (
    <>
      <div className="pm-review-cards-primary-row">
        <ActionButton
          expand="block"
          className="pm-review-cards-primary"
          loading={confirm.isPending}
          onClick={() => confirm.mutate(item, { onSuccess: onResolved })}
          aria-label={t("reviewQueue.confirmNamesFor", { name: title })}
        >
          {t("reviewQueue.cards.correct")}
        </ActionButton>
      </div>
      {confirm.isError && <IonItem lines="none"><IonLabel className="pm-wrap pm-danger-text" role="alert">{t("reviewQueue.confirmNamesError")}</IonLabel></IonItem>}
    </>
  );
}

function RatingCard({ item, title, onResolved }: { item: ReviewQueueItem; title: string; onResolved: () => void }) {
  const { t } = useTranslation();
  const rate = useRateLine();
  if (!item.purchaseItemId) return null;
  return (
    <IonItem>
      <IonLabel className="pm-wrap">
        <RatingControl
          legend={t("impressions.quickLegend", { name: title })}
          value={null}
          disabled={rate.isPending}
          onChange={(rating) => rate.mutate({ purchaseItemId: item.purchaseItemId!, rating }, { onSuccess: onResolved })}
        />
        {rate.isError && <p className="pm-danger-text" role="alert">{t("impressions.saveFailed")}</p>}
      </IonLabel>
    </IonItem>
  );
}

/** «Открыть товар»: the card is remembered, so «Назад» from the product shows it again. */
function OpenProductRow({ item, title, placeOnReturn }: { item: ReviewQueueItem; title: string; placeOnReturn: number }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const returnState = useReturnState();
  const rememberLine = useRememberLine();
  if (!item.productId) return null;
  const label = t("reviewQueue.openProduct");
  return (
    <IonItem
      button
      detail
      lines="none"
      onClick={() => {
        rememberLine(placeOnReturn);
        navigate(`/products/${encodeURIComponent(item.productId!)}`, { state: returnState });
      }}
      aria-label={`${label}: ${title}`}
    >
      <IonLabel color="primary">{label}</IonLabel>
    </IonItem>
  );
}
