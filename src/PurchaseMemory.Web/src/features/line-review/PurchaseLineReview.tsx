import { IonButton, IonButtons, IonContent, IonHeader, IonModal, IonTitle, IonToolbar } from "@ionic/react";
import { CircleCheck, X } from "lucide-react";
import { useEffect, useId, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import type { BoundingBox, ReceiptLineKind, TextPolygon } from "../../api/types";
import { HistoryLayerScope, useHistoryLayer } from "../../design-system/history/historyLayers";
import { LineProductMedia } from "../product-scan/LineProductMedia";
import { ProductLinkRows, type LinkChoice } from "../purchases/ProductLinkRows";
import type { LineViewerPhoto } from "../receipt-photo/LinePhoto";
import { isDiscount } from "../receipts/discountLines";
import type { EditableLine } from "../receipts/LinesEditor";
import { LineCarousel } from "./LineCarousel";
import { productNumberOf, stepLinePosition } from "./carouselSteps";
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
  /**
   * Saves corrected lines in one go (a product and a discount in its card can
   * both be changed); they then count as checked, and so do the lines at
   * `alsoApprove` (a product whose discount was corrected and approved).
   * Rejects with a message to show.
   */
  onSaveLines: (changes: LineChange[], alsoApprove?: number[]) => Promise<void>;
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

/** A corrected line: its position and its fields as they are now. */
export interface LineChange {
  index: number;
  patch: ReviewLinePatch;
}

/** Corrections not saved yet, by line position (the shown product and the discounts in its card). */
type Drafts = Record<number, ReviewLinePatch>;

/**
 * The saved purchase's lines in the line carousel, in a full-screen window.
 * Unlike a receipt draft, every change is saved to the purchase: the corrected
 * lines of a card are saved when the person moves on (or presses «Верно,
 * дальше», which then counts as checked), and «Удалить» (or «Убрать» on a
 * discount, after a question) saves the purchase without the line.
 * «Checked» marks are kept on the phone at once and reach the server in
 * batches (see `approvalSync.ts`).
 */
export function PurchaseLineReview({ start, onClose, lines, isApproved, setApproved, onSaveLines, onRemoveLine, photo, currency, receiptTotal, onOpenProduct }: PurchaseLineReviewProps) {
  const { t } = useTranslation();
  const titleId = useId();
  const isOpen = start !== null && lines.length > 0;
  const [index, setIndex] = useState(0);
  const [drafts, setDrafts] = useState<Drafts>({});
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [pickerOpen, setPickerOpen] = useState(false);

  const [lastNonce, setLastNonce] = useState<number | null>(null);
  const nonce = start?.nonce ?? null;
  if (nonce !== lastNonce) {
    setLastNonce(nonce);
    if (start && lines.length > 0) {
      const from = start.index !== null && start.index >= 0 && start.index < lines.length ? start.index : firstProblemIndex(lines);
      // A discount inside a product's card opens that card.
      setIndex(stepLinePosition(lines, from));
      setDrafts({});
      setError(null);
      setPickerOpen(false);
    }
  }

  const current = Math.min(index, Math.max(lines.length - 1, 0));
  const shown = lines.map((line, position) => drafts[position] ? { ...line, ...drafts[position] } : line);
  const changes: LineChange[] = Object.entries(drafts)
    .map(([position, patch]) => ({ index: Number(position), patch }))
    .filter(({ index: position, patch }) => lines[position] !== undefined && !isUnchanged(patch, lines[position]))
    .sort((left, right) => left.index - right.index);
  const dirty = changes.length > 0;
  const approvedCount = lines.filter((line) => isApproved(line)).length;
  const progressText = t("lineReview.progress", { approved: approvedCount, total: lines.length });

  // A save or delete is on its way to the server (a ref: state lags a render behind).
  const inFlight = useRef(false);
  // Leaving the screen with the review open (a tab, a link elsewhere)
  // unmounts it without a dismiss: the corrected line is still sent, unless
  // its save is already under way (a second one would carry a stale version).
  const pending = useRef<{ changes: LineChange[]; save: typeof onSaveLines }>({ changes: [], save: onSaveLines });
  pending.current = { changes, save: onSaveLines };
  useEffect(() => () => {
    const { changes: last, save } = pending.current;
    if (last.length > 0 && !inFlight.current && last.every((change) => change.patch.name.trim())) void save(last).catch(() => undefined);
  }, []);

  const edit = (position: number, patch: Partial<ReviewLinePatch>) => {
    // The fields are read-only during a save; a change arriving anyway would be lost when it completes.
    if (inFlight.current) return;
    setError(null);
    setDrafts((value) => ({ ...value, [position]: { ...(value[position] ?? patchOf(lines[position])), ...patch } }));
  };

  /** Saves the corrected lines, if any; false when they could not be saved (the error shows). */
  const commit = async (alsoApprove: number[] = []): Promise<boolean> => {
    if (!dirty) { setDrafts({}); return true; }
    if (changes.some((change) => !change.patch.name.trim())) { setError(t("purchaseEdit.nameRequired")); return false; }
    if (inFlight.current) return false;
    inFlight.current = true;
    setSaving(true);
    setError(null);
    try {
      await onSaveLines(changes, alsoApprove);
      setDrafts({});
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
    if (!dirty) { setDrafts({}); go(); return; }
    if (await commit()) go();
  };
  const approve = (position: number, related: number[]): Promise<void> | void => {
    // The card's discounts are checked with their product; their marks go
    // out before a correction is saved, so they follow the saved lines.
    for (const other of related) if (lines[other] && !isApproved(lines[other])) setApproved(lines[other].id, true);
    // A saved correction counts as checked by itself; one that could not be saved keeps the line.
    // The product is checked with them even when only its discount was corrected.
    if (dirty) return commit([position, ...related]).then((saved) => { if (!saved) throw new Error("not saved"); });
    setApproved(lines[position].id, true);
  };
  const unapprove = (position: number, related: number[]) => {
    for (const other of [position, ...related]) if (lines[other]) setApproved(lines[other].id, false);
  };
  const remove = async (position: number) => {
    if (inFlight.current) return;
    inFlight.current = true;
    setSaving(true);
    setError(null);
    try {
      await onRemoveLine(position);
      // Corrections of the card's other lines stay, moved up past the removed line.
      setDrafts((value) => Object.fromEntries(Object.entries(value)
        .filter(([other]) => Number(other) !== position)
        .map(([other, patch]) => [Number(other) > position ? Number(other) - 1 : Number(other), patch])));
      // A discount removed from its product's card leaves the product shown.
      setIndex((shownIndex) => shownIndex > position ? shownIndex - 1 : Math.max(0, Math.min(shownIndex, lines.length - 2)));
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
  // The open review is one «назад» step (browser, iPhone swipe): it closes the same way as ✕.
  const layer = useHistoryLayer(isOpen, async () => {
    if (!(await commit())) return false;
    onClose();
  });

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
        <HistoryLayerScope value={layer}>
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
                onUnapprove={unapprove}
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
                        productLink: drafts[current] ? drafts[current].productLink ?? "auto" : saved.productId ? "existing" : "auto",
                        productId: line.productId ?? null,
                        productName: line.productName ?? null,
                        productNeedsReview: line.productNeedsReview
                      }}
                      number={productNumberOf(shown, current)}
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
        </HistoryLayerScope>
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
