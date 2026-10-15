import { IonLabel, IonNote } from "@ionic/react";
import { AlertTriangle, CircleCheck } from "lucide-react";
import { useTranslation } from "react-i18next";
import type { PurchaseView } from "../../api/types";
import { ItemLink } from "../../design-system/components";
import { issueCopy } from "../issues/issueText";
import { formatShortDate } from "./dates";
import { formatMoney } from "./money";
import { useReturnState } from "../../app/backNavigation";

/**
 * One saved purchase as a list row: store with a ✓ (or the issues by name
 * after a ⚠), date and item count, and the total on the right. The state's
 * word («Проверено» / «Нужно проверить») is kept for screen readers only.
 * Used by the feed and by «Последние покупки» on Home.
 */
export function PurchaseRow({ purchase }: { purchase: PurchaseView }) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage === "en" ? "en-GB" : "ru-RU";
  const needsAttention = purchase.issues.length > 0;
  const returnState = useReturnState();
  // Filled only while a category, brand or tag filter is on; the whole receipt
  // still opens, this just says which lines made it match.
  const matchedLines = purchase.items.filter((item) => item.matchesFilter === true);
  const date = purchase.purchasedAt
    ? formatShortDate(purchase.purchasedAt, locale, { time: true })
    : t("feed.unknownDate");
  const total = purchase.receiptTotal === null ? "—" : formatMoney(purchase.receiptTotal, purchase.currency ?? "PLN", locale);
  // Discounts are lines on the receipt, not items the user bought.
  const itemCount = purchase.items.filter((item) => item.kind !== "discount").length;

  return (
    <ItemLink to={`/purchases/${purchase.id}`} state={returnState}>
      <IonLabel className="pm-wrap">
        <h2>
          {purchase.merchantName ?? t("feed.unknownStore")}
          {!needsAttention && <CircleCheck size={15} className="pm-inline-icon pm-success-icon" aria-hidden="true" />}
        </h2>
        <p className="visually-hidden">{t(needsAttention ? "status.needsAttention" : "status.verified")}</p>
        <p>{date} · {t("feed.itemCount", { count: itemCount })}</p>
        {needsAttention && purchase.issues.map((issue) => (
          <p key={`${issue.type}:${issue.itemPosition ?? "receipt"}`} className="pm-warn-text">
            <AlertTriangle size={13} className="pm-inline-icon" aria-hidden="true" />{issueCopy(t, issue).title}
          </p>
        ))}
        {matchedLines.length > 0 && (
          <p>
            <span>{t("filters.matchedLines", { count: matchedLines.length })}</span>{" "}
            <span>{matchedLines.map((item) => item.name).join(", ")}</span>
          </p>
        )}
      </IonLabel>
      <IonNote slot="end" className="pm-amount">{total}</IonNote>
    </ItemLink>
  );
}
