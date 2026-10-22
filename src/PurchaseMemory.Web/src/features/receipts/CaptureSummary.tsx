import { Check, ListChecks } from "lucide-react";
import { useId, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import type { ReceiptSnapshot } from "../../api/types";
import { carouselSteps } from "../line-review/carouselSteps";
import { totalsCheck } from "../line-review/lineChecks";
import { formatShortDate } from "../purchases/dates";
import { formatMoney } from "../purchases/money";
import { isDiscount } from "./discountLines";

export interface CaptureSummaryProps {
  snapshot: ReceiptSnapshot;
  /** «Верно, дальше» marks, aligned with `snapshot.items`. */
  checked: boolean[];
  onBackToProducts: () => void;
  /** In the box when the lines do not match the receipt total, e.g. «Взять сумму строк». */
  totalsAction?: ReactNode;
  /** Under the totals: what the draft still lacks. */
  children?: ReactNode;
}

/**
 * The draft's last step, after the last product (or «Сохранить сейчас»):
 * store and date, how many products, the discounts and what the lines add
 * up to, and whether that matches the receipt total. «Сохранить покупку» is
 * the screen's bottom bar.
 */
export function CaptureSummary({ snapshot, checked, onBackToProducts, totalsAction = null, children }: CaptureSummaryProps) {
  const { t, i18n } = useTranslation();
  const titleId = useId();
  const locale = i18n.resolvedLanguage === "en" ? "en-GB" : "ru-RU";
  const currency = snapshot.currency?.trim().toUpperCase() || null;
  const money = (value: number) => formatMoney(value, currency, locale);
  const steps = carouselSteps(snapshot.items);
  const productSteps = steps.filter((step) => step.kind === "product");
  const allChecked = steps.length > 0 && steps.every((step) => checked[step.position] === true);
  const checkedProducts = productSteps.filter((step) => checked[step.position] === true).length;
  const discounts = snapshot.items.filter((item) => isDiscount(item));
  const discountTotal = Math.round(discounts.reduce((sum, item) => sum + Math.abs(item.lineTotal ?? 0), 0) * 100) / 100;
  const check = totalsCheck(snapshot.items, snapshot.receiptTotal);
  const store = snapshot.merchantName?.trim() || t("purchaseEdit.unknownStore");
  const date = snapshot.purchasedAt ? formatShortDate(snapshot.purchasedAt, locale, { time: true }) : t("purchaseEdit.unknownDate");

  return (
    <section className="pm-summary" aria-labelledby={titleId}>
      <div className="pm-summary-head">
        <span className="pm-summary-badge" data-done={allChecked} aria-hidden="true">
          {allChecked ? <Check size={28} strokeWidth={2.6} /> : <ListChecks size={26} />}
        </span>
        <h2 id={titleId} className="pm-summary-title">{allChecked ? t("receiptSummary.allChecked") : t("receiptSummary.title")}</h2>
        <p className="pm-summary-meta">{store} · {date}</p>
        {!allChecked && productSteps.length > 0 && (
          <p className="pm-summary-meta">{t("receiptSummary.checkedCount", { checked: checkedProducts, count: productSteps.length })}</p>
        )}
      </div>

      <dl className="pm-summary-rows">
        <div><dt>{t("receiptSummary.products")}</dt><dd>{productSteps.length}</dd></div>
        {discounts.length > 0 && <div><dt>{t("receiptSummary.discounts")}</dt><dd data-tone="success">−{money(discountTotal)}</dd></div>}
        <div><dt>{t("receiptSummary.linesTotal")}</dt><dd>{money(check.linesTotal)}</dd></div>
      </dl>

      {check.receiptTotal !== null && (check.matches
        ? <p className="pm-summary-match" role="status"><Check size={16} aria-hidden="true" />{t("receiptSummary.matches")}</p>
        : (
          <div className="pm-summary-mismatch" role="status">
            <p><strong>{t("receiptSummary.receiptTotal", { total: money(check.receiptTotal) })}</strong></p>
            <p>{t("receiptSummary.mismatch")}</p>
            {totalsAction}
          </div>
        ))}

      {children}

      <button type="button" className="pm-footer-link pm-summary-back" onClick={onBackToProducts}>{t("receiptSummary.backToProducts")}</button>
    </section>
  );
}
