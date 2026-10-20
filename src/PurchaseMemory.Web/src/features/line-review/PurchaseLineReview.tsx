import { IonButton, IonButtons, IonContent, IonHeader, IonModal, IonTitle, IonToolbar } from "@ionic/react";
import { CircleCheck, X } from "lucide-react";
import { useEffect, useId, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import type { BoundingBox, ReceiptLineKind, TextPolygon } from "../../api/types";
import { LineProductMedia } from "../product-scan/LineProductMedia";
import { ProductLinkRows, type LinkChoice } from "../purchases/ProductLinkRows";
import type { LineViewerPhoto } from "../receipt-photo/LinePhoto";
import { isDiscount } from "../receipts/discountLines";
import type { EditableLine } from "../receipts/LinesEditor";
import { LineCarousel } from "./LineCarousel";
import { firstProblemIndex } from "./lineChecks";

/** What the review needs from a purchase line. */
export interface ReviewLine {
  id: string;
  name: string;
  quantity: number | null;
  unit: string | null;
  unitPrice: number | null;
  lineTotal: number | null;
  kind: ReceiptLineKind;
  boundingBox?: BoundingBox | null;
  /** The line's printed words on the photo, when the server found them. */
  textPolygons?: TextPolygon[] | null;
  productName?: string | null;
  /** The linked product; its photos and barcodes can be added under the line. */
  productId?: string | null;
  /** The linked product carries the «проверить» flag. */
  productNeedsReview?: boolean;
  approved?: boolean;
}

/** What a correction can change: the line itself and, through «Изменить товар», its product. */
export type ReviewLinePatch = Pick<EditableLine, "name" | "quantity" | "unit" | "unitPrice" | "lineTotal"> & Partial<LinkChoice>;

/**
 * How the review opens: at a tapped line (`index`), or (`index: null`, the
 * header button) at the first line with a problem, else the first line.
 * `nonce` makes a new opening at the same line a new session.
 */
export interface ReviewStart {
  index: number | null;
  nonce: number;
}

export interface PurchaseLineReviewProps {
  /** Null keeps the review closed. */
  start: ReviewStart | null;
  onClose: () => void;
  lines: ReviewLine[];
  isApproved: (line: ReviewLine) => boolean;
  setApproved: (lineId: string, approved: boolean) => void;
  /** Saves a corrected line; the line then counts as checked. Rejects with a message to show. */
  onSaveLine: (index: number, patch: ReviewLinePatch) => Promise<void>;
  /** Saves the purchase without this line. Rejects with a message to show. */
  onRemoveLine: (index: number) => Promise<void>;
  /** Null: the purchase has no receipt photo. */
  photo: LineViewerPhoto | null;
  currency: string | null;
  receiptTotal: number | null;
  /**
   * Opens a line's product card. The screen remembers which line was open, so
   * «Назад» brings the review back to the same line. Omitted: no such link.
   */
  onOpenProduct?: (productId: string, lineIndex: number) => void;
}

interface Draft {
  index: number;
  patch: ReviewLinePatch;
}

/**
 * The saved purchase's lines in the line carousel, in a full-screen window.
 * Unlike a receipt draft, every change is saved to the purchase: a corrected
 * line is saved when the person moves on (or presses «Верно, дальше», which
 * then counts as checked), and «Удалить» saves the purchase without the line.
 * «Checked» marks are kept on the phone at once and reach the server in
 * batches (see `approvalSync.ts`).
 */
export function PurchaseLineReview({ start, onClose, lines, isApproved, setApproved, onSaveLine, onRemoveLine, photo, currency, receiptTotal, onOpenProduct }: PurchaseLineReviewProps) {
  const { t } = useTranslation();
  const titleId = useId();
  const isOpen = start !== null && lines.length > 0;
  const [index, setIndex] = useState(0);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [pickerOpen, setPickerOpen] = useState(false);

  const [lastNonce, setLastNonce] = useState<number | null>(null);
  const nonce = start?.nonce ?? null;
  if (nonce !== lastNonce) {
    setLastNonce(nonce);
    if (start && lines.length > 0) {
      const from = start.index !== null && start.index >= 0 && start.index < lines.length ? start.index : firstProblemIndex(lines);
      setIndex(from);
      setDraft(null);
      setError(null);
      setPickerOpen(false);
    }
  }

  const current = Math.min(index, Math.max(lines.length - 1, 0));
  const shown = lines.map((line, position) => draft && draft.index === position ? { ...line, ...draft.patch } : line);
  const dirty = draft !== null && lines[draft.index] !== undefined && !isUnchanged(draft.patch, lines[draft.index]);
  const approvedCount = lines.filter((line) => isApproved(line)).length;
  const progressText = t("lineReview.progress", { approved: approvedCount, total: lines.length });

  // A save or delete is on its way to the server (a ref: state lags a render behind).
  const inFlight = useRef(false);
  // Leaving the screen with the review open (browser «Назад», another tab)
  // unmounts it without a dismiss: the corrected line is still sent, unless
  // its save is already under way (a second one would carry a stale version).
  const pending = useRef<{ draft: Draft | null; dirty: boolean; save: typeof onSaveLine }>({ draft: null, dirty: false, save: onSaveLine });
  pending.current = { draft, dirty, save: onSaveLine };
  useEffect(() => () => {
    const { draft: last, dirty: changed, save } = pending.current;
    if (last && changed && !inFlight.current && last.patch.name.trim()) void save(last.index, last.patch).catch(() => undefined);
  }, []);

  const edit = (position: number, patch: Partial<ReviewLinePatch>) => {
    // The fields are read-only during a save; a change arriving anyway would be lost when it completes.
    if (inFlight.current) return;
    setError(null);
    setDraft((value) => {
      const base = value && value.index === position ? value.patch : patchOf(lines[position]);
      return { index: position, patch: { ...base, ...patch } };
    });
  };

  /** Saves the corrected line, if any; false when it could not be saved (the error shows). */
  const commit = async (): Promise<boolean> => {
    if (!draft || !dirty) { setDraft(null); return true; }
    if (!draft.patch.name.trim()) { setError(t("purchaseEdit.nameRequired")); return false; }
    if (inFlight.current) return false;
    inFlight.current = true;
    setSaving(true);
    setError(null);
    try {
      await onSaveLine(draft.index, draft.patch);
      setDraft(null);
      return true;
    } catch (failure) {
      setError(messageOf(failure, t("lineReview.saveFailed")));
      return false;
    } finally {
      inFlight.current = false;
      setSaving(false);
    }
  };

  const move = async (next: number) => {
    const go = () => { setIndex(next); setPickerOpen(false); };
    // Nothing to save: move at once.
    if (!dirty) { setDraft(null); go(); return; }
    if (await commit()) go();
  };
  const approve = (position: number): Promise<void> | void => {
    // A saved correction counts as checked by itself; one that could not be saved keeps the line.
    if (dirty) return commit().then((saved) => { if (!saved) throw new Error("not saved"); });
    setApproved(lines[position].id, true);
  };
  const remove = async (position: number) => {
    if (inFlight.current) return;
    inFlight.current = true;
    setSaving(true);
    setError(null);
    try {
      await onRemoveLine(position);
      setDraft(null);
      setIndex(Math.max(0, Math.min(position, lines.length - 2)));
    } catch (failure) {
      setError(messageOf(failure, t("lineReview.saveFailed")));
    } finally {
      inFlight.current = false;
      setSaving(false);
    }
  };
  const close = async () => {
    if (await commit()) onClose();
  };
  // Every way of closing (✕, Escape, a swipe down, the hardware back) saves the corrected line first; a failed save keeps the window open.
  const canDismiss = () => commit();

  const line = shown[current];
  const saved = lines[current];

  return (
    <IonModal isOpen={isOpen} canDismiss={canDismiss} onDidDismiss={onClose} aria-labelledby={titleId} className="pm-review-modal">
      <IonHeader>
        <IonToolbar>
          <IonButtons slot="start">
            <IonButton className="pm-icon-button" disabled={saving} onClick={() => void close()} aria-label={t("lineReview.close")}><X size={22} aria-hidden="true" /></IonButton>
          </IonButtons>
          <IonTitle>
            <h2 id={titleId} className="pm-title-heading">
              <span className="visually-hidden">{t("lineReview.title")}. </span>
              <span className="pm-review-progress" aria-label={progressText}>
                <CircleCheck size={16} aria-hidden="true" />
                <span aria-hidden="true">{approvedCount}/{lines.length}</span>
              </span>
            </h2>
          </IonTitle>
        </IonToolbar>
      </IonHeader>
      <IonContent color="light">
        {line && saved && (
          <div className="pm-review-carousel">
            <LineCarousel
              lines={shown}
              index={current}
              onIndexChange={(next) => void move(next)}
              onChange={(position, patch) => edit(position, pickPatch(patch))}
              onRemove={remove}
              isChecked={(position) => lines[position] !== undefined && isApproved(lines[position])}
              onApprove={approve}
              onUnapprove={(position) => setApproved(lines[position].id, false)}
              currency={currency}
              receiptTotal={receiptTotal}
              photo={photo}
              busy={saving}
              error={error}
              extra={!isDiscount(saved) && (
                <div className="pm-carousel-extra">
                  {/* Every line has a product; it can be changed right here, saved with the line. */}
                  <ProductLinkRows
                    line={{
                      name: line.name,
                      productLink: draft?.index === current ? draft.patch.productLink ?? "auto" : saved.productId ? "existing" : "auto",
                      productId: line.productId ?? null,
                      productName: line.productName ?? null,
                      productNeedsReview: line.productNeedsReview
                    }}
                    number={current + 1}
                    pickerOpen={pickerOpen}
                    onPickerOpenChange={setPickerOpen}
                    onChoose={(choice) => edit(current, choice)}
                    // Leaving with unsaved changes would lose them: the link shows only while nothing is changed.
                    onOpenProduct={onOpenProduct && !dirty && saved.productId ? () => onOpenProduct(saved.productId!, current) : undefined}
                  />
                  {/* Photos and barcodes go to the product the line is saved with right now. */}
                  <LineProductMedia
                    productId={saved.productId}
                    productName={saved.productName}
                    changed={(line.productId ?? null) !== (saved.productId ?? null)}
                  />
                </div>
              )}
            />
          </div>
        )}
      </IonContent>
    </IonModal>
  );
}

function patchOf(line: ReviewLine): ReviewLinePatch {
  return {
    name: line.name,
    quantity: line.quantity,
    unit: line.unit,
    unitPrice: line.unitPrice,
    lineTotal: line.lineTotal,
    productLink: line.productId ? "existing" : "auto",
    productId: line.productId ?? null,
    productName: line.productName ?? null,
    productNeedsReview: Boolean(line.productNeedsReview)
  };
}

function isUnchanged(draft: ReviewLinePatch, line: ReviewLine): boolean {
  return draft.name === line.name
    && draft.quantity === line.quantity
    && draft.unit === line.unit
    && draft.unitPrice === line.unitPrice
    && draft.lineTotal === line.lineTotal
    && (draft.productId ?? null) === (line.productId ?? null)
    && (draft.productLink ?? (line.productId ? "existing" : "auto")) === (line.productId ? "existing" : "auto");
}

function pickPatch(patch: Partial<EditableLine>): Partial<ReviewLinePatch> {
  const result: Partial<ReviewLinePatch> = {};
  if ("name" in patch) result.name = patch.name;
  if ("quantity" in patch) result.quantity = patch.quantity;
  if ("unit" in patch) result.unit = patch.unit;
  if ("unitPrice" in patch) result.unitPrice = patch.unitPrice;
  if ("lineTotal" in patch) result.lineTotal = patch.lineTotal;
  return result;
}

function messageOf(failure: unknown, fallback: string): string {
  return failure instanceof Error && failure.message ? failure.message : fallback;
}
