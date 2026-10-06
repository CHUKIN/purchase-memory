import { BadgePercent, Trash2 } from "lucide-react";
import { useTranslation } from "react-i18next";
import { Field } from "../../design-system/components";
import styles from "./ReceiptDiscountLine.module.css";

export interface ReceiptDiscountLineProps {
  /** The printed discount label, e.g. "OPUST". */
  name: string;
  /** Stored negative (or null when unreadable), exactly as it sums with the other lines. */
  lineTotal: number | null;
  currency: string | null;
  /** The product line the discount reduces; null when no product is printed above it. */
  productName: string | null;
  /** That product's printed line total, used for the "after discount" hint. */
  productLineTotal?: number | null;
  /** A discount on the whole receipt: shown as such, calmly, with no product. */
  appliesToReceipt?: boolean;
  /** Makes the line editable; the amount is edited as a positive size and stored negative. */
  onChange?: (change: { name: string; lineTotal: number | null }) => void;
  onDelete?: () => void;
}

/**
 * A receipt discount shown as its own line under the product it belongs to
 * (owner decision, variant A). The product keeps its printed price; the
 * discount shows how much came off and what the product really cost.
 */
export function ReceiptDiscountLine({
  name,
  lineTotal,
  currency,
  productName,
  productLineTotal = null,
  appliesToReceipt = false,
  onChange,
  onDelete
}: ReceiptDiscountLineProps) {
  const { t, i18n } = useTranslation();
  const money = (value: number) => formatMoney(value, currency, i18n.language);
  const size = lineTotal === null ? null : Math.abs(lineTotal);
  const amountText = size === null ? t("discount.unknownAmount") : `−${money(size)}`;
  const label = name || t("discount.label");
  // A non-positive "real price" means the pairing is wrong, not a free item.
  const afterValue = productName && productLineTotal !== null && size !== null ? productLineTotal - size : null;
  const after = afterValue !== null && afterValue > 0 ? afterValue : null;
  const accessibleName = appliesToReceipt
    ? t("discount.accessibleNameReceipt", { name: label, amount: amountText })
    : productName
      ? t("discount.accessibleName", { name: label, amount: amountText, product: productName })
      : t("discount.accessibleNameUnlinked", { name: label, amount: amountText });

  return (
    <article className={styles.discount} aria-label={accessibleName} data-testid="receipt-discount-line">
      <div className={styles.header}>
        <span className={styles.label}>
          <BadgePercent aria-hidden="true" size={16} strokeWidth={2.25} />
          {t("discount.label")}
          {appliesToReceipt ? (
            <span className={styles.target}>{t("discount.onReceipt")}</span>
          ) : productName ? (
            <span className={styles.target}>{t("discount.forProduct", { product: productName })}</span>
          ) : (
            <span className={`${styles.target} ${styles.unlinked}`}>{t("discount.unlinked")}</span>
          )}
        </span>
        {onDelete && (
          <button type="button" className={styles.delete} aria-label={t("discount.delete", { name: label })} onClick={onDelete}>
            <Trash2 aria-hidden="true" size={18} />
          </button>
        )}
      </div>

      {onChange ? (
        <div className={styles.fields}>
          <Field label={t("discount.name")} value={name} onChange={(event) => onChange({ name: event.currentTarget.value, lineTotal })} />
          <Field
            label={t("discount.size")}
            type="number"
            inputMode="decimal"
            min={0}
            step="0.01"
            value={size ?? ""}
            onChange={(event) => {
              const raw = event.currentTarget.value;
              onChange({ name, lineTotal: raw === "" ? null : -Math.abs(Number(raw)) });
            }}
          />
        </div>
      ) : (
        <div className={styles.summary}>
          <p className={styles.name}>{name}</p>
          <span className={styles.amount}>{amountText}</span>
        </div>
      )}

      {after !== null && (
        <p className={styles.after}>
          {t("discount.after")} <strong>{money(after)}</strong>
        </p>
      )}
    </article>
  );
}

function formatMoney(value: number, currency: string | null, locale: string): string {
  if (currency && /^[A-Z]{3}$/.test(currency)) {
    return new Intl.NumberFormat(locale, { style: "currency", currency }).format(value);
  }
  return new Intl.NumberFormat(locale, { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value);
}
