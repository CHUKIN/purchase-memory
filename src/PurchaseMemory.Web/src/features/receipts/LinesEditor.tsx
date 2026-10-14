import {
  IonInput,
  IonItem,
  IonItemOption,
  IonItemOptions,
  IonItemSliding,
  IonLabel,
  IonList,
  IonNote
} from "@ionic/react";
import { Plus, ScanSearch, Trash2 } from "lucide-react";
import { useRef, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import type { BoundingBox, ReceiptLineKind } from "../../api/types";
import { DecimalInput, Footnote, SectionHeader, Sheet } from "../../design-system/components";
import { formatMoney, formatQuantity } from "../purchases/money";
import { ReceiptLineViewer, type LineViewerPhoto } from "../receipt-photo/ReceiptLineViewer";
import { discountLinks, isDiscount } from "./discountLines";
import { DiscountFields, ReceiptDiscountLine } from "./ReceiptDiscountLine";

/** What both editors (a receipt draft and a saved purchase) share about a line. */
export interface EditableLine {
  name: string;
  quantity: number | null;
  unit: string | null;
  unitPrice: number | null;
  lineTotal: number | null;
  /** Omitted by older receipt payloads, which means a product line. */
  kind?: ReceiptLineKind;
  /** Where recognition found the line on the receipt photo; kept through edits. */
  boundingBox?: BoundingBox | null;
}

/** The field to focus when a line sheet opens from a review-queue link. */
export type LineFocus = "name" | "price";

export interface LinesEditorProps<T extends EditableLine> {
  lines: T[];
  currency: string | null;
  /** The line whose sheet is open, by position; null when none. */
  openLine: number | null;
  onOpenLine: (index: number | null) => void;
  onChange: (index: number, patch: Partial<T>) => void;
  onRemove: (index: number) => void;
  /** «Добавить строку» / «Добавить скидку»; each kind listed gets its own row. */
  onAdd: (kind: ReceiptLineKind) => void;
  addKinds?: ReceiptLineKind[];
  /** Extra grey text under a product row, e.g. its catalog product. */
  describe?: (line: T, index: number) => ReactNode;
  /** Extra rows at the bottom of a product line's sheet, e.g. choosing its product. */
  sheetExtra?: (line: T, index: number) => ReactNode;
  focusField?: LineFocus | null;
  /** Section header above the rows. */
  title: string;
  /**
   * The receipt photo, when there is one: each line's sheet then offers
   * «Показать на фото», which opens the line viewer over it.
   */
  photo?: LineViewerPhoto | null;
}

/**
 * Receipt lines as an iOS list: name, «2 × 3,79» and the sum on each row;
 * a tap opens the line in a sheet, a swipe left deletes it. Discounts sit
 * under their product as their own rows. Used by the receipt review and the
 * purchase editor; with a receipt photo, a line's sheet also shows it there.
 */
export function LinesEditor<T extends EditableLine>({
  lines,
  currency,
  openLine,
  onOpenLine,
  onChange,
  onRemove,
  onAdd,
  addKinds = ["product"],
  describe,
  sheetExtra,
  focusField = null,
  title,
  photo = null
}: LinesEditorProps<T>) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage === "en" ? "en-GB" : "ru-RU";
  const money = (value: number | null) => value === null ? "—" : formatMoney(value, currency, locale);
  const links = discountLinks(lines);
  // Swiped rows stay open; a deleted row's neighbour must not inherit that state.
  const sliding = useRef<({ close?: () => Promise<unknown> } | null)[]>([]);
  const remove = (index: number) => {
    void sliding.current[index]?.close?.();
    onRemove(index);
  };
  const open = openLine !== null && lines[openLine] ? lines[openLine] : null;
  const [viewerLine, setViewerLine] = useState<number | null>(null);

  return (
    <>
      <SectionHeader>{title}</SectionHeader>
      <IonList inset className="pm-after-header">
        {lines.map((line, index) => {
          const number = index + 1;
          const target = links[index].target;
          return (
            <IonItemSliding key={(line as { key?: string }).key ?? index} ref={(element) => { sliding.current[index] = element as { close?: () => Promise<unknown> } | null; }}>
              <IonItem button detail={false} onClick={() => onOpenLine(index)}>
                {isDiscount(line)
                  ? (
                    <ReceiptDiscountLine
                      name={line.name}
                      lineTotal={line.lineTotal}
                      currency={currency}
                      productName={target === null ? null : lines[target].name || null}
                      productLineTotal={target === null ? null : lines[target].lineTotal}
                      appliesToReceipt={links[index].appliesToReceipt}
                    />
                  )
                  : (
                    <>
                      <IonLabel className="pm-wrap">
                        <h3>{line.name || t("lines.untitled")}</h3>
                        <p>
                          {line.quantity === null ? "—" : formatQuantity(line.quantity, locale)}{line.unit ? ` ${line.unit}` : ""} × {money(line.unitPrice)}
                          {line.unitPrice === null && <> · <span className="pm-warn-text">{t("purchaseEdit.missingPrice")}</span></>}
                        </p>
                        {describe?.(line, index)}
                      </IonLabel>
                      <IonNote slot="end" className="pm-amount">{money(line.lineTotal)}</IonNote>
                    </>
                  )}
              </IonItem>
              <IonItemOptions side="end" onIonSwipe={() => remove(index)}>
                <IonItemOption color="danger" expandable onClick={() => remove(index)} aria-label={t("purchaseEdit.removeLine", { number })}>
                  <Trash2 size={20} aria-hidden="true" />
                  <span className="pm-option-text">{t("ui.delete")}</span>
                </IonItemOption>
              </IonItemOptions>
            </IonItemSliding>
          );
        })}
        {addKinds.map((kind) => (
          <IonItem key={kind} button detail={false} onClick={() => onAdd(kind)} lines={kind === addKinds.at(-1) ? "none" : undefined}>
            <Plus slot="start" size={22} aria-hidden="true" className="pm-row-icon" />
            <IonLabel color="primary">{kind === "discount" ? t("purchaseEdit.addDiscount") : t("purchaseEdit.addLine")}</IonLabel>
          </IonItem>
        ))}
      </IonList>
      {lines.length > 0 && <Footnote>{t("ui.swipeToDelete")}</Footnote>}

      <Sheet
        isOpen={open !== null}
        onClose={() => onOpenLine(null)}
        title={open && isDiscount(open) ? t("purchaseEdit.discountLine", { number: (openLine ?? 0) + 1 }) : t("purchaseEdit.line", { number: (openLine ?? 0) + 1 })}
        initialBreakpoint={0.85}
      >
        {open !== null && openLine !== null && (
          <LineFields
            line={open}
            onChange={(patch) => onChange(openLine, patch as Partial<T>)}
            focusField={focusField}
            extra={!isDiscount(open) ? sheetExtra?.(open, openLine) : null}
          />
        )}
        {open !== null && openLine !== null && photo && (
          <IonList inset>
            <IonItem button detail={false} lines="none" onClick={() => setViewerLine(openLine)}>
              <ScanSearch slot="start" size={22} aria-hidden="true" className="pm-row-icon" />
              <IonLabel color="primary">{t("lineLocator.showOnPhoto")}</IonLabel>
            </IonItem>
          </IonList>
        )}
        {open !== null && openLine !== null && (
          <IonList inset>
            <IonItem button detail={false} lines="none" onClick={() => { onRemove(openLine); onOpenLine(null); }}>
              <IonLabel color="danger">{isDiscount(open) ? t("discount.delete", { name: open.name || t("discount.label") }) : t("purchaseEdit.removeLine", { number: openLine + 1 })}</IonLabel>
            </IonItem>
          </IonList>
        )}
      </Sheet>

      {photo && (
        <ReceiptLineViewer
          lines={lines}
          openIndex={viewerLine}
          onClose={() => setViewerLine(null)}
          photo={photo}
          currency={currency}
        />
      )}
    </>
  );
}

function LineFields({ line, onChange, focusField, extra }: { line: EditableLine; onChange: (patch: Partial<EditableLine>) => void; focusField: LineFocus | null; extra: ReactNode }) {
  const { t } = useTranslation();
  const nameRef = useRef<HTMLIonInputElement>(null);
  const priceRef = useRef<HTMLIonInputElement>(null);
  const focus = focusField === "price" ? priceRef : focusField === "name" ? nameRef : null;
  // Ionic inputs are ready a moment after the sheet starts to open.
  const focusOnce = useRef(false);
  if (focus && !focusOnce.current) {
    focusOnce.current = true;
    window.setTimeout(() => void focus.current?.setFocus?.(), 350);
  }

  if (isDiscount(line)) {
    return (
      <IonList inset>
        <DiscountFields name={line.name} lineTotal={line.lineTotal} onChange={onChange} />
      </IonList>
    );
  }

  return (
    <>
      <IonList inset>
        <IonItem>
          <IonInput
            ref={nameRef}
            label={t("purchaseEdit.name")}
            labelPlacement="stacked"
            value={line.name}
            required
            placeholder={t("lines.namePlaceholder")}
            className={line.name.trim() ? undefined : "ion-invalid"}
            onIonInput={(event) => onChange({ name: String(event.detail.value ?? "") })}
          />
        </IonItem>
      </IonList>
      <IonList inset>
        <IonItem>
          <DecimalInput label={t("purchaseEdit.quantity")} value={line.quantity} onChange={(quantity) => onChange({ quantity })} />
          {line.unit && <IonNote slot="end">{line.unit}</IonNote>}
        </IonItem>
        <IonItem>
          <DecimalInput inputRef={priceRef} label={t("legacyCapture.price")} value={line.unitPrice} invalid={line.unitPrice === null} onChange={(unitPrice) => onChange({ unitPrice })} />
        </IonItem>
        <IonItem lines="none">
          <DecimalInput label={t("legacyCapture.amount")} value={line.lineTotal} onChange={(lineTotal) => onChange({ lineTotal })} />
        </IonItem>
      </IonList>
      {extra}
    </>
  );
}
