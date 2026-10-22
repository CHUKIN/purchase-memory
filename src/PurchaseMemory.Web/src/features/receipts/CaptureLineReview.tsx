import { useTranslation } from "react-i18next";
import type { PurchaseDraftIssue, ReceiptItemSnapshot, ReceiptSnapshot } from "../../api/types";
import { Notice } from "../../design-system/components";
import { issueCopy, issueKey } from "../issues/issueText";
import { LineCarousel } from "../line-review/LineCarousel";
import { formatShortDate } from "../purchases/dates";
import { formatMoney } from "../purchases/money";
import type { LineViewerPhoto } from "../receipt-photo/LinePhoto";
import { CaptureSummary } from "./CaptureSummary";
import { ReviewHistory } from "./ReviewHistory";

export interface CaptureLineReviewProps {
  snapshot: ReceiptSnapshot;
  /** The line shown in the carousel. */
  index: number;
  onIndexChange: (index: number) => void;
  onChangeItem: (index: number, patch: Partial<ReceiptItemSnapshot>) => void;
  onRemoveItem: (index: number) => void;
  /** Puts a discount taken out with «Убрать» back at its place. */
  onRestoreItem: (index: number, item: ReceiptItemSnapshot) => void;
  /** «Верно, дальше» marks, aligned with `snapshot.items`; kept on this screen until the purchase is saved. */
  checked: boolean[];
  /** Marks (or unmarks) a product together with the discounts in its card. */
  onCheckedChange: (indexes: number[], checked: boolean) => void;
  onUseLinesTotal: (total: number) => void;
  /** The issues the server still reports after the last «Сохранить покупку» (shown before «Всё равно сохранить»). */
  remainingIssues: PurchaseDraftIssue[] | null;
  photo: LineViewerPhoto | null;
  receiptId: string;
  /** The summary step («Всё проверено») instead of the products. */
  summary: boolean;
  /** «Верно, к итогу» on the last product. */
  onShowSummary: () => void;
  /** «Вернуться к товарам» in the summary. */
  onBackToProducts: () => void;
}

/**
 * A recognized receipt before it is saved: the store, date and total in one
 * line, then the receipt's products in the carousel, checked and corrected in
 * place (a product's discounts inside its card), and after the last one the
 * summary with «Сохранить покупку». Store, date and total themselves are
 * edited in the full editor («Магазин, дата и итог», at the bottom of the
 * screen with «Сохранить сейчас»).
 */
export function CaptureLineReview({
  snapshot,
  index,
  onIndexChange,
  onChangeItem,
  onRemoveItem,
  onRestoreItem,
  checked,
  onCheckedChange,
  onUseLinesTotal,
  remainingIssues,
  photo,
  receiptId,
  summary,
  onShowSummary,
  onBackToProducts
}: CaptureLineReviewProps) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage === "en" ? "en-GB" : "ru-RU";
  const currency = snapshot.currency?.trim().toUpperCase() || null;
  const complete = snapshot.items.length > 0 && snapshot.items.every((item) => item.lineTotal !== null);
  // From the draft as it is now, so a filled-in field stops being reported at once.
  // The lines' sums are covered by the totals line and the per-line banners.
  const warnings = draftWarnings(snapshot).map((key) => t(key));
  const linesTotal = Math.round(snapshot.items.reduce((sum, item) => sum + (item.lineTotal ?? 0), 0) * 100) / 100;
  const linesTotalAction = complete && snapshot.receiptTotal !== null && (
    <button type="button" className="pm-carousel-link" onClick={() => onUseLinesTotal(linesTotal)}>
      {t("issues.useLinesTotal", { value: formatMoney(linesTotal, currency, locale) })}
    </button>
  );
  const notices = (
    <>
      {warnings.length > 0 && (
        <div className="pm-capture-warnings" role="status">
          <p><strong>{t("legacyCapture.needsReview")}</strong></p>
          <ul>{warnings.map((warning) => <li key={warning}>{warning}</li>)}</ul>
        </div>
      )}

      {remainingIssues && remainingIssues.length > 0 && (
        <Notice tone="warning" role="status">
          <p><strong>{t("legacyCapture.needsReview")}</strong></p>
          <ul className="pm-notice-list">{remainingIssues.map((issue) => <li key={issueKey(issue)}>{issueCopy(t, issue).title}</li>)}</ul>
        </Notice>
      )}

      {snapshot.items.length === 0 && <p className="pm-capture-empty">{t("receiptReview.noLines")}</p>}
    </>
  );

  if (summary) {
    return (
      <>
        <CaptureSummary snapshot={snapshot} checked={checked} onBackToProducts={onBackToProducts} totalsAction={linesTotalAction}>
          {notices}
        </CaptureSummary>
        <ReviewHistory receiptId={receiptId} />
      </>
    );
  }

  return (
    <>
      <div className="pm-capture-summary">
        <h2 className="pm-capture-store">{snapshot.merchantName?.trim() || t("purchaseEdit.unknownStore")}</h2>
        <p className="pm-capture-meta">
          {snapshot.purchasedAt ? formatShortDate(snapshot.purchasedAt, locale, { time: true }) : t("purchaseEdit.unknownDate")}
          {" · "}
          {snapshot.receiptTotal === null ? "—" : formatMoney(snapshot.receiptTotal, currency, locale)}
        </p>
      </div>

      {notices}

      <LineCarousel
        lines={snapshot.items}
        index={index}
        onIndexChange={onIndexChange}
        onChange={(position, patch) => onChangeItem(position, patch)}
        onRemove={onRemoveItem}
        onRestore={onRestoreItem}
        isChecked={(position) => checked[position] === true}
        onApprove={(position, related) => onCheckedChange([position, ...related], true)}
        onUnapprove={(position, related) => onCheckedChange([position, ...related], false)}
        onFinish={onShowSummary}
        currency={currency}
        receiptTotal={snapshot.receiptTotal}
        photo={photo}
        totalsAction={linesTotalAction}
      />

      <ReviewHistory receiptId={receiptId} />
    </>
  );
}

/** The receipt-level fields the draft still lacks, as i18n keys. */
export function draftWarnings(snapshot: ReceiptSnapshot): string[] {
  const keys: string[] = [];
  if (!snapshot.merchantName?.trim()) keys.push("receiptReview.missingStore");
  if (!snapshot.purchasedAt) keys.push("receiptReview.missingDate");
  if (!snapshot.currency?.trim()) keys.push("receiptReview.missingCurrency");
  if (snapshot.receiptTotal === null) keys.push("receiptReview.missingTotal");
  return keys;
}
