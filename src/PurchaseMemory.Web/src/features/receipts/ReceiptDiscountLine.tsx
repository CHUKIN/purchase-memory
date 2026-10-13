import { IonInput, IonItem, IonLabel, IonNote } from "@ionic/react";
import { BadgePercent } from "lucide-react";
import { useTranslation } from "react-i18next";
import { DecimalInput } from "../../design-system/components";

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
}

/**
 * A receipt discount as the content of its own list row, under the product
 * it belongs to (owner decision, variant A). The product keeps its printed
 * price; the discount shows how much came off and what the product really
 * cost. Render it inside an `IonItem`: the amount goes to the row's end slot.
 */
export function ReceiptDiscountLine({
  name,
  lineTotal,
  currency,
  productName,
  productLineTotal = null,
  appliesToReceipt = false
}: ReceiptDiscountLineProps) {
  const { t, i18n } = useTranslation();
  const money = (value: number) => formatDiscountMoney(value, currency, i18n.language);
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
    <>
      <IonLabel className="pm-wrap">
        <article aria-label={accessibleName} data-testid="receipt-discount-line" className="pm-discount">
          <h3 className="pm-discount-title">
            <BadgePercent aria-hidden="true" size={15} strokeWidth={2.25} />
            {t("discount.label")}{" "}
            {appliesToReceipt
              ? <span>{t("discount.onReceipt")}</span>
              : productName
                ? <span>{t("discount.forProduct", { product: productName })}</span>
                : <span className="pm-warn-text">{t("discount.unlinked")}</span>}
          </h3>
          <p>{name}</p>
          {after !== null && <p>{t("discount.after")} <strong>{money(after)}</strong></p>}
        </article>
      </IonLabel>
      <IonNote slot="end" className="pm-amount pm-discount-amount">{amountText}</IonNote>
    </>
  );
}

/**
 * The discount's own fields in the line sheet. The size is typed as a
 * positive amount and stored negative, the way it sums with the other lines.
 */
export function DiscountFields({ name, lineTotal, onChange }: {
  name: string;
  lineTotal: number | null;
  onChange: (change: { name: string; lineTotal: number | null }) => void;
}) {
  const { t } = useTranslation();
  const size = lineTotal === null ? null : Math.abs(lineTotal);
  return (
    <>
      <IonItem>
        <IonInput label={t("discount.name")} labelPlacement="stacked" value={name} onIonInput={(event) => onChange({ name: String(event.detail.value ?? ""), lineTotal })} />
      </IonItem>
      <IonItem>
        <DecimalInput label={t("discount.size")} value={size} onChange={(value) => onChange({ name, lineTotal: value === null ? null : -Math.abs(value) })} />
      </IonItem>
    </>
  );
}

export function formatDiscountMoney(value: number, currency: string | null, locale: string): string {
  if (currency && /^[A-Z]{3}$/.test(currency)) {
    return new Intl.NumberFormat(locale, { style: "currency", currency }).format(value);
  }
  return new Intl.NumberFormat(locale, { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value);
}
