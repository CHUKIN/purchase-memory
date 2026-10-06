import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useId, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import {
  createImpression,
  deleteImpression,
  listProductImpressions,
  listProductRateableLines,
  listPurchaseImpressions,
  listUnratedPurchaseItems
} from "../../api/client";
import type { Impression, ImpressionRating, PurchaseDetailItem } from "../../api/types";
import { Button, Surface } from "../../design-system/components";
import { formatMoney } from "../purchases/money";
import { ImpressionForm, type ImpressionDraft } from "./ImpressionForm";
import { ImpressionHistory, StoreComparison } from "./ImpressionHistory";
import { authorLabel, groupByAuthor, impressionsQueryKey, ratingLabel } from "./impressionText";
import { RatingControl, RatingIcon } from "./RatingControl";
import { HelpLink } from "../help/HelpLink";
import styles from "./Impressions.module.css";

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
  const headingId = useId();
  const selectId = useId();
  const impressions = useQuery({ queryKey: [...impressionsQueryKey, "product", productId], queryFn: () => listProductImpressions(productId) });
  const lines = useQuery({ queryKey: [...impressionsQueryKey, "rateable", productId], queryFn: () => listProductRateableLines(productId) });
  const { create, remove } = useImpressionMutations();
  const [chosenLine, setChosenLine] = useState<string | null>(null);
  const lineId = chosenLine ?? lines.data?.[0]?.purchaseItemId ?? null;

  return (
    <Surface as="section" className={styles.section} aria-labelledby={headingId}>
      <h2 id={headingId}>{t("impressions.title")}</h2>
      <HelpLink topic="impressions" />

      {lines.data && lines.data.length > 0 && (
        <ImpressionForm
          disabled={!lineId}
          onSubmit={(draft) => create.mutateAsync({ purchaseItemId: lineId!, draft })}
        >
          <label htmlFor={selectId}>
            {t("impressions.whichPurchase")}
            <select id={selectId} value={lineId ?? ""} onChange={(event) => setChosenLine(event.target.value)}>
              {lines.data.map((line) => {
                const price = line.unitPrice ?? line.lineTotal;
                return (
                  <option key={line.purchaseItemId} value={line.purchaseItemId}>
                    {[line.merchantName ?? t("impressions.unknownStore"), formatDay(locale, line.purchasedAt), price === null ? null : formatMoney(price, line.currency, locale)]
                      .filter(Boolean).join(" · ")}
                  </option>
                );
              })}
            </select>
          </label>
        </ImpressionForm>
      )}
      {lines.data?.length === 0 && <p className={styles.muted}>{t("impressions.noPurchasesToRate")}</p>}

      {impressions.isError && <p className={styles.error} role="alert">{t("impressions.loadFailed")}</p>}
      {impressions.data && (
        <>
          <ImpressionHistory impressions={impressions.data} locale={locale} onDelete={(id) => remove.mutateAsync(id)} />
          {impressions.data.length > 0 && (
            <section className={styles.section} aria-label={t("impressions.comparisonTitle")}>
              <h3>{t("impressions.comparisonTitle")}</h3>
              <StoreComparison impressions={impressions.data} />
            </section>
          )}
        </>
      )}
    </Surface>
  );
}

/** Impressions of one purchase, fetched once and shared by all its lines. */
export function usePurchaseImpressions(purchaseId: string) {
  return useQuery({ queryKey: [...impressionsQueryKey, "purchase", purchaseId], queryFn: () => listPurchaseImpressions(purchaseId) });
}

/** Under a purchase line: each person's latest opinion of it and an «Оценить» form. */
export function PurchaseLineImpressions({ item, impressions, locale }: { item: PurchaseDetailItem; impressions: Impression[]; locale: string }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const { create, remove } = useImpressionMutations();
  const mine = impressions.filter((impression) => impression.purchaseItemId === item.id);
  const latest = groupByAuthor(t, mine).map((group) => ({ group, impression: group.impressions[0] }));

  return (
    <div className={styles.section}>
      {latest.length > 0 && (
        <ul className={styles.lineSummary} aria-label={t("impressions.lineSummary", { name: item.name })}>
          {latest.map(({ group, impression }) => (
            <li key={group.authorUserId} className={styles.badge} data-rating={impression.rating}>
              <RatingIcon rating={impression.rating} />
              {authorLabel(t, impression)}: {ratingLabel(t, impression.rating)}
            </li>
          ))}
        </ul>
      )}
      {open
        ? (
          <>
            <ImpressionForm
              onSubmit={(draft) => create.mutateAsync({ purchaseItemId: item.id, draft })}
              onCancel={() => setOpen(false)}
            />
            {mine.length > 0 && (
              <ImpressionHistory impressions={mine} locale={locale} headingLevel={4} onDelete={(id) => remove.mutateAsync(id)} />
            )}
          </>
        )
        : (
          <div>
            <Button intent="quiet" onClick={() => setOpen(true)} aria-label={t("impressions.rateLine", { name: item.name })}>
              {t("impressions.rate")}
            </Button>
          </div>
        )}
    </div>
  );
}

/**
 * «Оценить недавнее» on Home: recent lines the viewer has not rated, one tap
 * each. It never blocks anything: on error or when empty it simply is not shown.
 */
export function UnratedPurchasesBlock({ locale }: { locale: string }) {
  const { t } = useTranslation();
  const headingId = useId();
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
    <Surface as="section" className={styles.section} aria-labelledby={headingId}>
      <h2 id={headingId}>{t("impressions.unratedTitle")}</h2>
      <p className={styles.muted}>{t("impressions.unratedIntro")}</p>
      <HelpLink topic="impressions" />
      <ul className={styles.unratedList}>
        {unrated.data.map((item) => {
          const price = item.unitPrice ?? item.lineTotal;
          return (
            <li key={item.purchaseItemId} className={styles.unratedItem}>
              <Link to={item.productId ? `/products/${encodeURIComponent(item.productId)}` : `/purchases/${encodeURIComponent(item.purchaseId)}`}>
                {item.productName ?? item.itemName}
              </Link>
              <p className={styles.muted}>
                {[item.merchantName ?? t("impressions.unknownStore"), formatDay(locale, item.purchasedAt), price === null ? null : formatMoney(price, item.currency, locale)]
                  .filter(Boolean).join(" · ")}
              </p>
              <RatingControl
                legend={t("impressions.quickLegend", { name: item.productName ?? item.itemName })}
                hideLegend
                value={null}
                disabled={create.isPending}
                onChange={(rating) => void rate(item.purchaseItemId, rating)}
              />
              {failedItem === item.purchaseItemId && <p className={styles.error} role="alert">{t("impressions.saveFailed")}</p>}
            </li>
          );
        })}
      </ul>
    </Surface>
  );
}
