import { IonButton, IonItem, IonLabel, IonList } from "@ionic/react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { SmilePlus } from "lucide-react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import {
  createImpression,
  deleteImpression,
  listProductImpressions,
  listProductRateableLines,
  listPurchaseImpressions,
  listUnratedPurchaseItems
} from "../../api/client";
import type { Impression, ImpressionRating, PurchaseDetailItem } from "../../api/types";
import { Link } from "react-router";
import { Footnote, SectionHeader, SelectRow, Sheet } from "../../design-system/components";
import { formatShortDate } from "../purchases/dates";
import { formatMoney } from "../purchases/money";
import { HelpLink } from "../help/HelpLink";
import { ImpressionForm, type ImpressionDraft } from "./ImpressionForm";
import { ImpressionHistory, StoreComparison } from "./ImpressionHistory";
import { authorLabel, groupByAuthor, impressionsQueryKey, ratingLabel } from "./impressionText";
import { RatingControl, RatingIcon } from "./RatingControl";
import { useReturnState } from "../../app/backNavigation";

function formatDay(locale: string, value: string): string {
  return new Intl.DateTimeFormat(locale, { dateStyle: "medium" }).format(new Date(value));
}

/** Every impression list and prompt reads from the same data, so any change refreshes them all. */
function useImpressionMutations() {
  const queryClient = useQueryClient();
  const refresh = () => queryClient.invalidateQueries({ queryKey: impressionsQueryKey });
  const create = useMutation({
    mutationFn: ({ purchaseItemId, draft }: { purchaseItemId: string; draft: ImpressionDraft }) =>
      createImpression({ purchaseItemId, rating: draft.rating, note: draft.note, photo: draft.photo }),
    onSuccess: refresh
  });
  const remove = useMutation({ mutationFn: deleteImpression, onSuccess: refresh });
  return { create, remove };
}

/** The product page's «Впечатления»: rate one of its purchases, each person's history, and stores side by side. */
export function ProductImpressionsSection({ productId, locale }: { productId: string; locale: string }) {
  const { t } = useTranslation();
  const impressions = useQuery({ queryKey: [...impressionsQueryKey, "product", productId], queryFn: () => listProductImpressions(productId) });
  const lines = useQuery({ queryKey: [...impressionsQueryKey, "rateable", productId], queryFn: () => listProductRateableLines(productId) });
  const { create, remove } = useImpressionMutations();
  const [chosenLine, setChosenLine] = useState<string | null>(null);
  const lineId = chosenLine ?? lines.data?.[0]?.purchaseItemId ?? null;
  const lineText = (line: NonNullable<typeof lines.data>[number]) => {
    const price = line.unitPrice ?? line.lineTotal;
    return [line.merchantName ?? t("impressions.unknownStore"), formatDay(locale, line.purchasedAt), price === null ? null : formatMoney(price, line.currency, locale)]
      .filter(Boolean).join(" · ");
  };
  const chosen = lines.data?.find((line) => line.purchaseItemId === lineId);

  return (
    <section aria-labelledby="product-impressions-title">
      <SectionHeader id="product-impressions-title" action={<HelpLink topic="impressions" />}>{t("impressions.title")}</SectionHeader>

      {lines.data && lines.data.length > 0 && (
        <ImpressionForm disabled={!lineId} onSubmit={(draft) => create.mutateAsync({ purchaseItemId: lineId!, draft })}>
          <SelectRow
            label={t("impressions.whichPurchase")}
            valueText={chosen ? lineText(chosen) : ""}
            value={lineId ?? ""}
            onChange={setChosenLine}
            groups={[{ options: lines.data.map((line) => ({ value: line.purchaseItemId, label: lineText(line) })) }]}
          />
        </ImpressionForm>
      )}
      {lines.data?.length === 0 && <Footnote>{t("impressions.noPurchasesToRate")}</Footnote>}

      {impressions.isError && <Footnote tone="danger" role="alert">{t("impressions.loadFailed")}</Footnote>}
      {impressions.data && (
        <>
          <ImpressionHistory impressions={impressions.data} locale={locale} onDelete={(id) => remove.mutateAsync(id)} />
          {impressions.data.length > 0 && (
            <section aria-labelledby="impressions-comparison-title">
              <SectionHeader id="impressions-comparison-title" level={3}>{t("impressions.comparisonTitle")}</SectionHeader>
              <StoreComparison impressions={impressions.data} />
            </section>
          )}
        </>
      )}
    </section>
  );
}

/** Impressions of one purchase, fetched once and shared by all its lines. */
export function usePurchaseImpressions(purchaseId: string) {
  return useQuery({ queryKey: [...impressionsQueryKey, "purchase", purchaseId], queryFn: () => listPurchaseImpressions(purchaseId) });
}

/**
 * Under a purchase line: each person's latest opinion of it, and «Оценить»,
 * which opens a sheet with the form and this line's history.
 */
export function PurchaseLineImpressions({ item, impressions, locale }: { item: PurchaseDetailItem; impressions: Impression[]; locale: string }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const { create, remove } = useImpressionMutations();
  const mine = impressions.filter((impression) => impression.purchaseItemId === item.id);
  const latest = groupByAuthor(t, mine).map((group) => ({ group, impression: group.impressions[0] }));

  return (
    <div className="pm-line-impressions">
      {latest.length > 0 && (
        <ul className="pm-chips" aria-label={t("impressions.lineSummary", { name: item.name })}>
          {latest.map(({ group, impression }) => (
            <li key={group.authorUserId} className="pm-chip" data-rating={impression.rating}>
              <RatingIcon rating={impression.rating} size={12} />
              {authorLabel(t, impression)}<span className="visually-hidden">: {ratingLabel(t, impression.rating)}</span>
            </li>
          ))}
        </ul>
      )}
      {/* «Оценить» as an icon: four lines would otherwise repeat the same word four times. */}
      <IonButton fill="clear" className="pm-icon-button pm-rate-button" onClick={() => setOpen(true)} aria-label={t("impressions.rateLine", { name: item.name })}>
        <SmilePlus size={20} aria-hidden="true" />
      </IonButton>
      <Sheet isOpen={open} onClose={() => setOpen(false)} title={t("impressions.rateLine", { name: item.name })}>
        <ImpressionForm onSubmit={(draft) => create.mutateAsync({ purchaseItemId: item.id, draft })} />
        {mine.length > 0 && <ImpressionHistory impressions={mine} locale={locale} headingLevel={4} onDelete={(id) => remove.mutateAsync(id)} />}
      </Sheet>
    </div>
  );
}

/**
 * «Оценить недавнее» on Home: recent lines the viewer has not rated, one tap
 * each. It never blocks anything: on error or when empty it simply is not shown.
 */
export function UnratedPurchasesBlock({ locale }: { locale: string }) {
  const { t } = useTranslation();
  const returnState = useReturnState();
  const unrated = useQuery({ queryKey: [...impressionsQueryKey, "unrated"], queryFn: () => listUnratedPurchaseItems() });
  const { create } = useImpressionMutations();
  const [failedItem, setFailedItem] = useState<string | null>(null);

  if (!unrated.data || unrated.data.length === 0) return null;

  const rate = async (purchaseItemId: string, rating: ImpressionRating) => {
    setFailedItem(null);
    try {
      await create.mutateAsync({ purchaseItemId, draft: { rating, note: "", photo: null } });
    } catch {
      setFailedItem(purchaseItemId);
    }
  };

  return (
    <section aria-labelledby="unrated-title">
      <SectionHeader id="unrated-title" action={<HelpLink topic="impressions" />}>{t("impressions.unratedTitle")}</SectionHeader>
      <IonList inset className="pm-after-header">
        {unrated.data.map((item) => {
          const price = item.unitPrice ?? item.lineTotal;
          const name = item.productName ?? item.itemName;
          return (
            <IonItem key={item.purchaseItemId}>
              <IonLabel className="pm-wrap">
                <h3>
                  <Link className="pm-text-link" to={item.productId ? `/products/${encodeURIComponent(item.productId)}` : `/purchases/${encodeURIComponent(item.purchaseId)}`} state={returnState}>
                    {name}
                  </Link>
                </h3>
                <p>
                  {[item.merchantName ?? t("impressions.unknownStore"), formatShortDate(item.purchasedAt, locale), price === null ? null : formatMoney(price, item.currency, locale)]
                    .filter(Boolean).join(" · ")}
                </p>
                <RatingControl
                  legend={t("impressions.quickLegend", { name })}
                  hideLegend
                  value={null}
                  disabled={create.isPending}
                  onChange={(rating) => void rate(item.purchaseItemId, rating)}
                />
                {failedItem === item.purchaseItemId && <p className="pm-danger-text" role="alert">{t("impressions.saveFailed")}</p>}
              </IonLabel>
            </IonItem>
          );
        })}
      </IonList>
    </section>
  );
}
