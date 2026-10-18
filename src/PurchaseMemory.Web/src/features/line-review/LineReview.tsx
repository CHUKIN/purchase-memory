import { IonButton, IonButtons, IonContent, IonHeader, IonModal, IonTitle, IonToolbar } from "@ionic/react";
import { Check, CircleCheck, Pencil, RotateCcw, Undo2, X } from "lucide-react";
import { useEffect, useId, useRef, useState, type KeyboardEvent, type PointerEvent } from "react";
import { useTranslation } from "react-i18next";
import type { BoundingBox, ReceiptLineKind, TextPolygon } from "../../api/types";
import { ActionButton, Notice, Sheet } from "../../design-system/components";
import { formatMoney, formatQuantity } from "../purchases/money";
import { LinePhoto, type LineViewerPhoto } from "../receipt-photo/LinePhoto";
import { hasLocation } from "../receipt-photo/photoBox";
import { discountLinks, isDiscount } from "../receipts/discountLines";
import { LineFields, type EditableLine } from "../receipts/LinesEditor";
import { LineProductMedia } from "../product-scan/LineProductMedia";
import { ProductLinkRows, productStatusText, type LinkChoice } from "../purchases/ProductLinkRows";

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
  /** The linked product; its photos and barcodes can be added from the line's sheet. */
  productId?: string | null;
  /** The linked product carries the «проверить» flag. */
  productNeedsReview?: boolean;
  approved?: boolean;
}

/**
 * The fields the swipe-left editor can change: the line itself and, through
 * «Изменить товар», its product (sent like the purchase editor's choice).
 */
export type ReviewLinePatch = Pick<EditableLine, "name" | "quantity" | "unit" | "unitPrice" | "lineTotal"> & Partial<LinkChoice>;

/**
 * How the review opens: at a tapped line (`index`), or (`index: null`, the
 * header button) at the first line nobody has checked. `nonce` makes a new
 * opening at the same line a new session.
 */
export interface ReviewStart {
  index: number | null;
  nonce: number;
}

export interface LineReviewProps {
  /** Null keeps the review closed. */
  start: ReviewStart | null;
  onClose: () => void;
  lines: ReviewLine[];
  isApproved: (line: ReviewLine) => boolean;
  setApproved: (lineId: string, approved: boolean) => void;
  /** Saves a corrected line; the line then counts as checked. Rejects with a message to show. */
  onSaveLine: (index: number, patch: ReviewLinePatch) => Promise<void>;
  /** Null: the purchase has no receipt photo. */
  photo: LineViewerPhoto | null;
  currency: string | null;
  /**
   * Opens a line's product card. The screen remembers which line was open, so
   * «Назад» brings the review back to the same card. Omitted: no such link.
   */
  onOpenProduct?: (productId: string, lineIndex: number) => void;
}

interface Session {
  order: number[];
  cursor: number;
  /** «Проверить ещё раз»: every line, checked or not. */
  all: boolean;
}

/** How long the card takes to fly off; the CSS transition matches it. */
const leaveMs = 220;

/** Lines nobody has checked, starting at `from` and wrapping round. */
function uncheckedOrder(lines: ReviewLine[], isApproved: (line: ReviewLine) => boolean, from: number | null): number[] {
  const start = from ?? 0;
  const indices = lines.map((_, index) => (start + index) % lines.length);
  return indices.filter((index) => index === from || !isApproved(lines[index]));
}

/**
 * Swipe review of a purchase's lines, one card at a time (Tinder-style): the
 * line on the receipt photo, outlined, with everything about the line under
 * it. Swipe right (or ✓, or →) — the line is right; swipe left (or ✎, or ←)
 * — correct it, and saving counts as checked. Marks are kept on the phone at
 * once and reach the server in batches (see `approvalSync.ts`).
 */
export function LineReview({ start, onClose, lines, isApproved, setApproved, onSaveLine, photo, currency, onOpenProduct }: LineReviewProps) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage === "en" ? "en-GB" : "ru-RU";
  const titleId = useId();
  const isOpen = start !== null && lines.length > 0;

  const [session, setSession] = useState<Session | null>(null);
  const [lastNonce, setLastNonce] = useState<number | null>(null);
  const nonce = start?.nonce ?? null;
  if (nonce !== lastNonce) {
    setLastNonce(nonce);
    if (start && lines.length > 0) {
      const from = start.index !== null && start.index >= 0 && start.index < lines.length ? start.index : null;
      setSession({ order: uncheckedOrder(lines, isApproved, from), cursor: 0, all: false });
    }
  }

  const [photoRatio, setPhotoRatio] = useState<number | null>(null);
  const [zoomed, setZoomed] = useState(false);
  const [leaving, setLeaving] = useState(false);
  const [dragX, setDragX] = useState(0);
  const [editing, setEditing] = useState<{ index: number; draft: ReviewLinePatch } | null>(null);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [pickerOpen, setPickerOpen] = useState(false);
  const cardRef = useRef<HTMLDivElement>(null);
  const rootRef = useRef<HTMLDivElement>(null);
  const drag = useRef<{ id: number; x: number; y: number; dragging: boolean; width: number } | null>(null);
  const activePointers = useRef(new Set<number>());
  const leaveTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => () => { if (leaveTimer.current) clearTimeout(leaveTimer.current); }, []);

  const total = lines.length;
  const approvedCount = lines.filter((line) => isApproved(line)).length;
  const current = session && session.cursor < session.order.length ? session.order[session.cursor] : null;
  const line = current !== null && current < lines.length ? lines[current] : null;
  const done = session !== null && line === null;

  // Keyboard users land on the card, so ← and → work at once.
  useEffect(() => {
    if (isOpen) rootRef.current?.focus({ preventScroll: true });
  }, [isOpen, nonce]);

  const advance = () => {
    setSession((value) => {
      if (!value) return value;
      let cursor = value.cursor + 1;
      // Lines checked in the meantime are not shown again (unless reviewing all).
      while (!value.all && cursor < value.order.length && isApproved(lines[value.order[cursor]])) cursor += 1;
      return { ...value, cursor };
    });
    setZoomed(false);
  };

  const approve = () => {
    if (!line || leaving || editing) return;
    setApproved(line.id, true);
    const reduced = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;
    if (reduced) { advance(); setDragX(0); return; }
    setLeaving(true);
    leaveTimer.current = setTimeout(() => {
      leaveTimer.current = null;
      setLeaving(false);
      setDragX(0);
      advance();
    }, leaveMs);
  };

  const edit = () => {
    if (!line || leaving || current === null) return;
    setDragX(0);
    setSaveError(null);
    setPickerOpen(false);
    setEditing({
      index: current,
      draft: {
        name: line.name,
        quantity: line.quantity,
        unit: line.unit,
        unitPrice: line.unitPrice,
        lineTotal: line.lineTotal,
        productLink: line.productId ? "existing" : "auto",
        productId: line.productId ?? null,
        productName: line.productName ?? null,
        productNeedsReview: Boolean(line.productNeedsReview)
      }
    });
  };

  const back = () => {
    if (!session || leaving || session.cursor === 0) return;
    setSession({ ...session, cursor: Math.min(session.cursor, session.order.length) - 1 });
    setZoomed(false);
  };

  const reviewAll = () => setSession({ order: lines.map((_, index) => index), cursor: 0, all: true });
  const continueUnchecked = () => setSession({ order: uncheckedOrder(lines, isApproved, null), cursor: 0, all: false });

  const saveEdit = async () => {
    if (!editing) return;
    if (!editing.draft.name.trim()) { setSaveError(t("purchaseEdit.nameRequired")); return; }
    setSaving(true);
    setSaveError(null);
    try {
      await onSaveLine(editing.index, editing.draft);
      setEditing(null);
      advance();
    } catch (error) {
      setSaveError(error instanceof Error && error.message ? error.message : t("lineReview.saveFailed"));
    } finally {
      setSaving(false);
    }
  };

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (editing || done || !line) return;
    const target = event.target as HTMLElement;
    if (target.closest("input, textarea, ion-input")) return;
    if (event.key === "ArrowRight") { event.preventDefault(); approve(); }
    else if (event.key === "ArrowLeft") { event.preventDefault(); edit(); }
    else if (event.key === "Backspace") { event.preventDefault(); back(); }
  };

  // Swipe: a mostly horizontal drag of the card. Off while the photo is
  // zoomed (one finger pans it then) and as soon as a second finger lands
  // (that is a pinch).
  const onPointerDown = (event: PointerEvent<HTMLDivElement>) => {
    activePointers.current.add(event.pointerId);
    if (activePointers.current.size > 1 || zoomed || leaving || editing) {
      drag.current = null;
      setDragX(0);
      return;
    }
    if ((event.target as HTMLElement).closest("button")) return;
    drag.current = { id: event.pointerId, x: event.clientX, y: event.clientY, dragging: false, width: cardRef.current?.offsetWidth || 320 };
  };
  const onPointerMove = (event: PointerEvent<HTMLDivElement>) => {
    const state = drag.current;
    if (!state || state.id !== event.pointerId) return;
    const dx = event.clientX - state.x;
    const dy = event.clientY - state.y;
    if (!state.dragging) {
      if (Math.abs(dy) > 12 && Math.abs(dy) > Math.abs(dx)) { drag.current = null; return; }
      if (Math.abs(dx) < 10) return;
      state.dragging = true;
    }
    setDragX(dx);
  };
  const onPointerEnd = (event: PointerEvent<HTMLDivElement>) => {
    activePointers.current.delete(event.pointerId);
    const state = drag.current;
    if (!state || state.id !== event.pointerId) return;
    drag.current = null;
    const dx = event.clientX - state.x;
    const threshold = Math.max(60, state.width * 0.25);
    if (event.type === "pointercancel" || !state.dragging) { setDragX(0); return; }
    if (dx > threshold) approve();
    else if (dx < -threshold) edit();
    else setDragX(0);
  };

  const money = (value: number | null) => value === null ? "—" : formatMoney(value, currency, locale);
  const progressText = t("lineReview.progress", { approved: approvedCount, total });

  return (
    <>
      <IonModal isOpen={isOpen} onDidDismiss={onClose} aria-labelledby={titleId} className="pm-review-modal">
        <IonHeader>
          <IonToolbar>
            <IonButtons slot="start">
              <IonButton className="pm-icon-button" onClick={onClose} aria-label={t("lineReview.close")}><X size={22} aria-hidden="true" /></IonButton>
            </IonButtons>
            <IonTitle>
              <h2 id={titleId} className="pm-title-heading">
                <span className="visually-hidden">{t("lineReview.title")}. </span>
                <span className="pm-review-progress" aria-label={progressText}>
                  <CircleCheck size={16} aria-hidden="true" />
                  <span aria-hidden="true">{approvedCount}/{total}</span>
                </span>
              </h2>
            </IonTitle>
          </IonToolbar>
        </IonHeader>
        <IonContent color="light" scrollY={false}>
          <div className="pm-review" ref={rootRef} tabIndex={-1} onKeyDown={onKeyDown}>
            {done && (
              <div className="pm-review-done">
                <CircleCheck size={56} aria-hidden="true" className="pm-review-done-icon" />
                <p className="pm-review-done-title" role="status">
                  {approvedCount === total ? t("lineReview.allChecked") : t("lineReview.remaining", { count: total - approvedCount })}
                </p>
                {approvedCount < total && (
                  <IonButton shape="round" onClick={continueUnchecked}>{t("lineReview.continue")}</IonButton>
                )}
                <IonButton fill="outline" shape="round" onClick={reviewAll}>
                  <RotateCcw size={18} aria-hidden="true" />
                  {t("lineReview.reviewAgain")}
                </IonButton>
                <IonButton fill="clear" onClick={onClose}>{t("ui.done")}</IonButton>
              </div>
            )}
            {line && current !== null && (
              <>
                <div
                  // A new element per line: the next card appears in place
                  // instead of sliding back from where the last one flew.
                  key={current}
                  ref={cardRef}
                  className="pm-review-card"
                  data-leaving={leaving}
                  data-dragging={dragX !== 0 && !leaving}
                  style={leaving ? undefined : { transform: `translateX(${dragX}px) rotate(${dragX / 30}deg)` }}
                  onPointerDown={onPointerDown}
                  onPointerMove={onPointerMove}
                  onPointerUp={onPointerEnd}
                  onPointerCancel={onPointerEnd}
                  data-testid="line-review-card"
                >
                  <div className="pm-review-stamp pm-review-stamp-ok" style={{ opacity: leaving ? 1 : Math.min(1, Math.max(0, dragX / 80)) }} aria-hidden="true"><Check size={28} /></div>
                  <div className="pm-review-stamp pm-review-stamp-edit" style={{ opacity: Math.min(1, Math.max(0, -dragX / 80)) }} aria-hidden="true"><Pencil size={24} /></div>
                  <ReviewPhoto
                    line={line}
                    index={current}
                    photo={photo}
                    photoRatio={photoRatio}
                    onPhotoRatio={setPhotoRatio}
                    onZoomChange={setZoomed}
                  />
                  <ReviewInfo
                    line={line}
                    index={current}
                    lines={lines}
                    money={money}
                    locale={locale}
                    approved={isApproved(line)}
                    onUnapprove={() => setApproved(line.id, false)}
                    onOpenProduct={onOpenProduct}
                  />
                </div>
                <div className="pm-review-actions">
                  <IonButton fill="clear" className="pm-review-back" disabled={!session || session.cursor === 0 || leaving} onClick={back} aria-label={t("lineReview.back")}>
                    <Undo2 size={22} aria-hidden="true" />
                  </IonButton>
                  <IonButton shape="round" className="pm-review-action pm-review-edit" onClick={edit} disabled={leaving} aria-label={t("lineReview.edit")}>
                    <Pencil size={26} aria-hidden="true" />
                  </IonButton>
                  <IonButton shape="round" className="pm-review-action pm-review-approve" onClick={approve} disabled={leaving} aria-label={t("lineReview.approve")}>
                    <Check size={30} aria-hidden="true" />
                  </IonButton>
                  <span className="pm-review-back" aria-hidden="true" />
                </div>
                <span className="visually-hidden" role="status" aria-live="polite">
                  {t("lineReview.announcement", { number: current + 1, count: total, name: line.name.trim() || t("lines.untitled"), amount: money(line.lineTotal) })}
                </span>
              </>
            )}
          </div>
        </IonContent>
      </IonModal>

      <Sheet
        isOpen={editing !== null}
        onClose={() => { if (!saving) setEditing(null); }}
        title={t("purchaseEdit.line", { number: (editing?.index ?? 0) + 1 })}
        start={<IonButton disabled={saving} onClick={() => setEditing(null)}>{t("ui.cancel")}</IonButton>}
        end={<ActionButton strong loading={saving} onClick={() => void saveEdit()}>{t("lineReview.saveLine")}</ActionButton>}
        initialBreakpoint={0.85}
      >
        {editing && (
          <>
            <LineFields
              line={{ ...editing.draft, kind: lines[editing.index]?.kind ?? "product" }}
              onChange={(patch) => setEditing((value) => value && { ...value, draft: { ...value.draft, ...pickPatch(patch) } })}
              focusField={null}
              extra={null}
            />
            {saveError && <Notice tone="danger" role="alert"><p>{saveError}</p></Notice>}
            {lines[editing.index] && !isDiscount(lines[editing.index]) && (
              <>
                {/* Every line has a product; it can be changed right here, saved with the line. */}
                <ProductLinkRows
                  line={{
                    name: editing.draft.name,
                    productLink: editing.draft.productLink ?? "auto",
                    productId: editing.draft.productId ?? null,
                    productName: editing.draft.productName ?? null,
                    productNeedsReview: editing.draft.productNeedsReview
                  }}
                  number={editing.index + 1}
                  pickerOpen={pickerOpen}
                  onPickerOpenChange={setPickerOpen}
                  onChoose={(choice) => setEditing((value) => value && { ...value, draft: { ...value.draft, ...choice } })}
                  // Leaving with unsaved changes would lose them: the link appears only while the draft equals the saved line.
                  onOpenProduct={onOpenProduct && isUnchanged(editing.draft, lines[editing.index]) && lines[editing.index].productId
                    ? () => onOpenProduct(lines[editing.index].productId!, editing.index)
                    : undefined}
                />
                {/* Photos and barcodes go to the product the line is saved with right now. */}
                <LineProductMedia
                  productId={lines[editing.index].productId}
                  productName={lines[editing.index].productName}
                  changed={(editing.draft.productId ?? null) !== (lines[editing.index].productId ?? null)}
                />
              </>
            )}
          </>
        )}
      </Sheet>
    </>
  );
}

function isUnchanged(draft: ReviewLinePatch, line: ReviewLine): boolean {
  return draft.name === line.name
    && draft.quantity === line.quantity
    && draft.unit === line.unit
    && draft.unitPrice === line.unitPrice
    && draft.lineTotal === line.lineTotal
    && (draft.productId ?? null) === (line.productId ?? null);
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

function ReviewPhoto({ line, index, photo, photoRatio, onPhotoRatio, onZoomChange }: {
  line: ReviewLine;
  index: number;
  photo: LineViewerPhoto | null;
  photoRatio: number | null;
  onPhotoRatio: (ratio: number) => void;
  onZoomChange: (zoomed: boolean) => void;
}) {
  const { t } = useTranslation();
  const name = line.name.trim() || t("lines.untitled");
  if (!photo) return <div className="pm-review-photo pm-review-photo-empty"><p>{t("lineReview.noPhoto")}</p></div>;
  if (!photo.photo) {
    return (
      <div className="pm-review-photo pm-review-photo-empty">
        {photo.failed
          ? <p role="alert">{t("lineReview.photoFailed")}</p>
          : <p>{t("lineReview.photoLoading")}</p>}
      </div>
    );
  }
  const note = !photo.photo.croppable
    ? t("lineReview.multiPart")
    : hasLocation(line.boundingBox, line.textPolygons) ? null : t("lineReview.noBox");
  return (
    <div className="pm-review-photo">
      <LinePhoto
        photo={photo.photo}
        box={line.boundingBox}
        shapes={line.textPolygons}
        focusAlt={t("lineReview.photoLineAlt", { name })}
        wholeAlt={t("lineReview.photoWholeAlt")}
        photoRatio={photoRatio}
        onPhotoRatio={onPhotoRatio}
        onZoomChange={onZoomChange}
        resetKey={index}
        note={note}
      />
    </div>
  );
}

function ReviewInfo({ line, index, lines, money, locale, approved, onUnapprove, onOpenProduct }: {
  line: ReviewLine;
  index: number;
  lines: ReviewLine[];
  money: (value: number | null) => string;
  locale: string;
  approved: boolean;
  onUnapprove: () => void;
  onOpenProduct?: LineReviewProps["onOpenProduct"];
}) {
  const { t } = useTranslation();
  const name = line.name.trim() || t("lines.untitled");
  const links = discountLinks(lines);
  const discount = isDiscount(line);
  const target = discount ? links[index].target : null;
  // A product's own discounts, with the price it came to.
  const discounts = discount ? [] : lines.filter((other, position) => isDiscount(other) && links[position].target === index);
  const afterDiscount = discounts.length > 0 && line.lineTotal !== null
    ? line.lineTotal + discounts.reduce((sum, other) => sum + (other.lineTotal ?? 0), 0)
    : null;

  return (
    <div className="pm-review-info">
      <div className="pm-review-meta">
        <span>{discount ? t("discount.label") : t("lineReview.lineNumber", { number: index + 1 })}</span>
        {approved && (
          <button type="button" className="pm-review-checked" onClick={onUnapprove} aria-label={t("lineReview.unapprove")}>
            <CircleCheck size={16} aria-hidden="true" />
            <span aria-hidden="true">{t("lineReview.checked")}</span>
          </button>
        )}
      </div>
      <h3 className="pm-review-name">{name}</h3>
      <div className="pm-review-figures">
        {discount
          ? <span>{target !== null ? t("discount.forProduct", { product: lines[target].name }) : links[index].appliesToReceipt ? t("lineReview.receiptDiscount") : t("discount.unlinked")}</span>
          : <span>{line.quantity === null ? "—" : formatQuantity(line.quantity, locale)}{line.unit ? ` ${line.unit}` : ""} × {money(line.unitPrice)}</span>}
        <strong className={discount ? "pm-discount-amount" : undefined}>{money(line.lineTotal)}</strong>
      </div>
      {discounts.map((other, position) => (
        <div key={position} className="pm-review-discount">
          <span>{other.name}</span>
          <span className="pm-discount-amount">{money(other.lineTotal)}</span>
        </div>
      ))}
      {afterDiscount !== null && (
        <div className="pm-review-discount"><span>{t("discount.after")}</span><strong>{money(afterDiscount)}</strong></div>
      )}
      {!discount && (
        <>
          <p className={line.productNeedsReview ? "pm-review-product pm-warn-text" : "pm-review-product"}>
            {line.productName
              ? productStatusText(t, { name: line.name, productLink: "existing", productName: line.productName, productNeedsReview: line.productNeedsReview })
              : t("purchaseEdit.productUnlinked")}
          </p>
          {line.productId && line.productName && onOpenProduct && (
            <button
              type="button"
              className="pm-review-open-product"
              onClick={() => onOpenProduct(line.productId!, index)}
              aria-label={t("purchaseEdit.openProduct", { name: line.productName })}
            >
              {t("purchaseEdit.openProductShort")}
            </button>
          )}
        </>
      )}
    </div>
  );
}
