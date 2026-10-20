import { IonAlert } from "@ionic/react";
import { AlertTriangle, Check, ChevronLeft, ChevronRight } from "lucide-react";
import { useId, useRef, useState, type PointerEvent, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { parseDecimal } from "../../design-system/components";
import { formatMoney, formatQuantity } from "../purchases/money";
import { LinePhoto, type LineViewerPhoto } from "../receipt-photo/LinePhoto";
import { hasLocation } from "../receipt-photo/photoBox";
import { discountLinks, isDiscount } from "../receipts/discountLines";
import type { EditableLine } from "../receipts/LinesEditor";
import { lineProblem, totalsCheck, type LineProblem } from "./lineChecks";

/** A horizontal drag longer than this (px) switches lines. */
export const swipeThreshold = 50;
/** At most this many dots (24 px each, fits a 320 px screen); with more lines they slide along with the current one. */
const maxDots = 8;

export interface LineCarouselProps<T extends EditableLine> {
  lines: T[];
  /** The line shown (controlled, so the screen can remember it and guard leaving it). */
  index: number;
  onIndexChange: (index: number) => void;
  /** A field of the shown line changed. */
  onChange: (index: number, patch: Partial<EditableLine>) => void;
  /** Called after the person confirmed «Удалить». Omitted: no delete button. */
  onRemove?: (index: number) => void | Promise<void>;
  isChecked: (index: number) => boolean;
  /** «Верно, дальше»: marks the line; the carousel then moves on unless this rejects. */
  onApprove: (index: number) => void | Promise<void>;
  /** Tapping «Проверено» takes the mark back. Omitted: the mark is just shown. */
  onUnapprove?: (index: number) => void;
  currency: string | null;
  /** The receipt's total, for the «do the lines add up» line. */
  receiptTotal: number | null;
  /** Null: there is no receipt photo at all. */
  photo: LineViewerPhoto | null;
  /** Saving or deleting: the actions wait. */
  busy?: boolean;
  /** Shown above the actions, e.g. a failed save. */
  error?: ReactNode;
  /** Next to the totals line, e.g. «Взять сумму строк». */
  totalsAction?: ReactNode;
  /** Under the actions: more about the shown line (its product, say). */
  extra?: ReactNode;
}

/**
 * The receipt's lines one at a time: the photo moves to the shown line and
 * highlights it, under it whether the lines add up, the line's position with
 * a dot per line, and the line itself, editable in place. «Верно, дальше»
 * marks it and moves on; a swipe (on the photo or the card), the chevrons or
 * a dot move freely. Used by the receipt draft and the saved purchase.
 */
export function LineCarousel<T extends EditableLine>({
  lines,
  index,
  onIndexChange,
  onChange,
  onRemove,
  isChecked,
  onApprove,
  onUnapprove,
  currency,
  receiptTotal,
  photo,
  busy = false,
  error = null,
  totalsAction = null,
  extra = null
}: LineCarouselProps<T>) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage === "en" ? "en-GB" : "ru-RU";
  const money = (value: number | null) => value === null ? "—" : formatMoney(value, currency, locale);
  const [photoRatio, setPhotoRatio] = useState<number | null>(null);
  const [zoomed, setZoomed] = useState(false);
  const [confirmingRemove, setConfirmingRemove] = useState(false);
  // Moves are animated only once the person has moved: the first layout (the photo measuring itself) is not.
  const [moved, setMoved] = useState(false);
  const regionId = useId();

  const count = lines.length;
  const current = Math.min(Math.max(index, 0), Math.max(count - 1, 0));
  const go = (next: number) => {
    if (busy || next < 0 || next >= count || next === current) return;
    setZoomed(false);
    setMoved(true);
    onIndexChange(next);
  };
  // A zoomed photo pans under one finger, so only the card swipes then.
  const photoSwipe = useSwipe((step) => go(current + step), zoomed);
  const cardSwipe = useSwipe((step) => go(current + step), false);

  const line = lines[current];
  if (!line) return null;
  const approve = () => {
    if (busy) return;
    const advance = () => { if (current + 1 < count) go(current + 1); };
    const result = onApprove(current);
    // A rejected approval (a correction that could not be saved) keeps the line; the screen shows why.
    if (result instanceof Promise) result.then(advance, () => undefined);
    else advance();
  };

  const problem = lineProblem(line);
  const checked = isChecked(current);
  const discount = isDiscount(line);
  const name = line.name.trim() || t("lines.untitled");
  const link = discount ? discountLinks(lines)[current] : null;
  const discountNote = !link
    ? null
    : link.target !== null
      ? t("discount.forProduct", { product: lines[link.target].name.trim() || t("lines.untitled") })
      : link.appliesToReceipt ? t("lineReview.receiptDiscount") : t("discount.unlinked");

  return (
    <section className="pm-carousel" aria-labelledby={regionId}>
      <h2 id={regionId} className="visually-hidden">{t("lineCarousel.title")}</h2>
      <TotalsLine lines={lines} receiptTotal={receiptTotal} money={money} action={totalsAction} />

      <div className="pm-carousel-photo" data-animate={moved} {...photoSwipe.handlers}>
        <CarouselPhoto line={line} index={current} name={name} photo={photo} photoRatio={photoRatio} onPhotoRatio={setPhotoRatio} onZoomChange={setZoomed} />
      </div>

      <div className="pm-carousel-nav">
        <button type="button" className="pm-carousel-chevron" onClick={() => go(current - 1)} disabled={current === 0 || busy} aria-label={t("lineCarousel.previous")}>
          <ChevronLeft size={24} aria-hidden="true" />
        </button>
        <div className="pm-carousel-position">
          <span className="pm-carousel-count">{t("lineCarousel.position", { number: current + 1, count })}</span>
          <Dots lines={lines} current={current} isChecked={isChecked} onGo={go} />
        </div>
        <button type="button" className="pm-carousel-chevron" onClick={() => go(current + 1)} disabled={current === count - 1 || busy} aria-label={t("lineCarousel.next")}>
          <ChevronRight size={24} aria-hidden="true" />
        </button>
      </div>

      <div className="pm-carousel-card" key={current} data-animate={moved} {...cardSwipe.handlers} data-testid="line-carousel-card">
        {problem && (
          <p className="pm-carousel-problem">
            <AlertTriangle size={16} aria-hidden="true" />
            <span>{problemText(t, problem, line, money, locale)}</span>
          </p>
        )}
        {discountNote && <p className="pm-carousel-note">{t("discount.label")} {discountNote}</p>}
        <label className="pm-carousel-field pm-carousel-name">
          <span>{discount ? t("discount.name") : t("purchaseEdit.name")}</span>
          <input
            type="text"
            value={line.name}
            placeholder={t("lines.namePlaceholder")}
            aria-invalid={line.name.trim() ? undefined : true}
            enterKeyHint="done"
            // While a correction is being saved the fields wait, so nothing typed meanwhile is lost.
            readOnly={busy}
            onChange={(event) => onChange(current, { name: event.target.value })}
          />
        </label>
        {discount
          ? (
            <div className="pm-carousel-figures pm-carousel-figures-single">
              <NumberField
                readOnly={busy}
                label={t("discount.size")}
                value={line.lineTotal === null ? null : Math.abs(line.lineTotal)}
                invalid={line.lineTotal === null}
                onChange={(value) => onChange(current, { lineTotal: value === null ? null : -Math.abs(value) })}
              />
            </div>
          )
          : (
            <div className="pm-carousel-figures">
              <NumberField
                readOnly={busy}
                label={line.unit ? t("lineCarousel.quantityUnit", { unit: line.unit }) : t("lineCarousel.quantity")}
                value={line.quantity}
                onChange={(quantity) => onChange(current, { quantity })}
              />
              <NumberField readOnly={busy} label={t("legacyCapture.price")} value={line.unitPrice} invalid={line.unitPrice === null} onChange={(unitPrice) => onChange(current, { unitPrice })} />
              <NumberField readOnly={busy} label={t("legacyCapture.amount")} value={line.lineTotal} invalid={line.lineTotal === null || problem?.kind === "arithmetic"} onChange={(lineTotal) => onChange(current, { lineTotal })} />
            </div>
          )}
      </div>

      {error && <div className="pm-carousel-error" role="alert">{error}</div>}

      <div className="pm-carousel-actions">
        {onRemove && (
          <button type="button" className="pm-carousel-remove" disabled={busy || count <= 1} onClick={() => setConfirmingRemove(true)}>
            {t("ui.delete")}
          </button>
        )}
        {checked
          ? onUnapprove
            ? (
              <button type="button" className="pm-carousel-checked" disabled={busy} onClick={() => onUnapprove(current)} aria-label={t("lineReview.unapprove")}>
                <Check size={18} aria-hidden="true" />
                <span aria-hidden="true">{t("lineReview.checked")}</span>
              </button>
            )
            : <span className="pm-carousel-checked"><Check size={18} aria-hidden="true" />{t("lineReview.checked")}</span>
          : (
            <button type="button" className="pm-carousel-approve" disabled={busy} onClick={approve}>
              <Check size={18} aria-hidden="true" />
              {current + 1 < count ? t("lineCarousel.approveNext") : t("lineCarousel.approveLast")}
            </button>
          )}
      </div>

      {extra}

      <span className="visually-hidden" role="status" aria-live="polite">
        {t("lineReview.announcement", { number: current + 1, count, name, amount: money(line.lineTotal) })}
      </span>

      <IonAlert
        isOpen={confirmingRemove}
        header={discount ? t("lineCarousel.removeDiscountTitle") : t("lineCarousel.removeTitle", { number: current + 1 })}
        message={name}
        buttons={[
          { text: t("ui.cancel"), role: "cancel" },
          { text: t("ui.delete"), role: "destructive", handler: () => { void onRemove?.(current); } }
        ]}
        onDidDismiss={() => setConfirmingRemove(false)}
      />
    </section>
  );
}

function problemText(
  t: ReturnType<typeof useTranslation>["t"],
  problem: LineProblem,
  line: EditableLine,
  money: (value: number | null) => string,
  locale: string
): string {
  switch (problem.kind) {
    case "missingPrice":
      return t("lineCarousel.problemMissingPrice");
    case "missingTotal":
      return t("lineCarousel.problemMissingTotal");
    case "arithmetic":
      return t("lineCarousel.problemArithmetic", {
        quantity: formatQuantity(line.quantity ?? 0, locale),
        price: money(line.unitPrice),
        expected: money(problem.expected),
        total: money(line.lineTotal)
      });
  }
}

/** «Сумма строк сходится с итогом чека», or by how much it does not. */
function TotalsLine({ lines, receiptTotal, money, action }: {
  lines: EditableLine[];
  receiptTotal: number | null;
  money: (value: number | null) => string;
  action: ReactNode;
}) {
  const { t } = useTranslation();
  const check = totalsCheck(lines, receiptTotal);
  const tone = check.receiptTotal === null ? "neutral" : check.matches ? "success" : "warning";
  let text: string;
  if (check.receiptTotal === null) text = t("lineCarousel.totalsNoTotal", { lines: money(check.linesTotal) });
  else if (check.matches) text = t("lineCarousel.totalsOk");
  else if ((check.difference ?? 0) < 0) text = t("lineCarousel.totalsShort", { lines: money(check.linesTotal), total: money(check.receiptTotal), difference: money(Math.abs(check.difference ?? 0)) });
  else text = t("lineCarousel.totalsOver", { lines: money(check.linesTotal), total: money(check.receiptTotal), difference: money(Math.abs(check.difference ?? 0)) });

  return (
    <div className="pm-carousel-totals" data-tone={tone}>
      <p role="status" aria-live="polite">
        {tone === "success" ? <Check size={14} aria-hidden="true" /> : tone === "warning" ? <AlertTriangle size={14} aria-hidden="true" /> : null}
        <span>
          {text}
          {check.missing > 0 && <> · {t("lineCarousel.totalsMissing", { count: check.missing })}</>}
        </span>
      </p>
      {!check.matches && action}
    </div>
  );
}

function Dots({ lines, current, isChecked, onGo }: {
  lines: EditableLine[];
  current: number;
  isChecked: (index: number) => boolean;
  onGo: (index: number) => void;
}) {
  const { t } = useTranslation();
  const first = Math.max(0, Math.min(current - Math.floor(maxDots / 2), lines.length - maxDots));
  const shown = lines.slice(first, first + maxDots);
  return (
    <div className="pm-carousel-dots">
      {shown.map((line, offset) => {
        const position = first + offset;
        const problem = lineProblem(line) !== null;
        const checked = isChecked(position);
        // A line the person confirmed counts as checked even if its figures still look odd.
        const state = position === current ? "current" : checked ? "checked" : problem ? "problem" : "plain";
        const suffix = checked ? t("lineCarousel.dotChecked") : problem ? t("lineCarousel.dotProblem") : "";
        return (
          <button
            key={position}
            type="button"
            className="pm-carousel-dot"
            data-state={state}
            aria-current={position === current ? "true" : undefined}
            aria-label={`${t("lineReview.lineNumber", { number: position + 1 })}${suffix}`}
            onClick={() => onGo(position)}
          >
            <span aria-hidden="true" />
          </button>
        );
      })}
    </div>
  );
}

function CarouselPhoto({ line, index, name, photo, photoRatio, onPhotoRatio, onZoomChange }: {
  line: EditableLine;
  index: number;
  name: string;
  photo: LineViewerPhoto | null;
  photoRatio: number | null;
  onPhotoRatio: (ratio: number) => void;
  onZoomChange: (zoomed: boolean) => void;
}) {
  const { t } = useTranslation();
  if (!photo) return <div className="pm-carousel-photo-empty"><p>{t("lineReview.noPhoto")}</p></div>;
  if (!photo.photo) {
    return (
      <div className="pm-carousel-photo-empty">
        {photo.failed ? <p role="alert">{t("lineReview.photoFailed")}</p> : <p>{t("lineReview.photoLoading")}</p>}
      </div>
    );
  }
  const note = !photo.photo.croppable
    ? t("lineReview.multiPart")
    : hasLocation(line.boundingBox, line.textPolygons) ? null : t("lineReview.noBox");
  return (
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
  );
}

/**
 * An amount or quantity in the card, typed with a comma or a point. While the
 * text is not a number yet (a lone «,») nothing is reported; a value changed
 * from outside replaces the text (like `DecimalInput`).
 */
function NumberField({ label, value, onChange, invalid = false, readOnly = false }: {
  label: string;
  value: number | null;
  onChange: (value: number | null) => void;
  invalid?: boolean;
  readOnly?: boolean;
}) {
  const { i18n } = useTranslation();
  const comma = i18n.resolvedLanguage !== "en";
  const format = (number: number | null) => number === null ? "" : comma ? String(number).replace(".", ",") : String(number);
  const [text, setText] = useState(() => format(value));
  const [shown, setShown] = useState(value);
  if (value !== shown) {
    setShown(value);
    if (parseDecimal(text) !== value) setText(format(value));
  }
  return (
    <label className="pm-carousel-field">
      <span>{label}</span>
      <input
        type="text"
        inputMode="decimal"
        enterKeyHint="done"
        readOnly={readOnly}
        value={text}
        aria-invalid={invalid || undefined}
        onChange={(event) => {
          const next = event.target.value;
          setText(next);
          const parsed = parseDecimal(next);
          if (parsed !== undefined) {
            setShown(parsed);
            onChange(parsed);
          }
        }}
      />
    </label>
  );
}

/**
 * A mostly horizontal drag of at least `swipeThreshold` px, reported as the
 * step to take: to the left is the next line (+1), to the right the previous
 * one (−1), like turning pages. It ignores drags that start on a field or a
 * button (text selection and taps stay theirs), stops at a second finger (a
 * pinch) and is off while `disabled`. Vertical drags are left to scrolling.
 */
function useSwipe(onStep: (step: 1 | -1) => void, disabled: boolean) {
  const start = useRef<{ id: number; x: number; y: number } | null>(null);
  const pointers = useRef(new Set<number>());

  const onPointerDown = (event: PointerEvent<HTMLElement>) => {
    pointers.current.add(event.pointerId);
    if (pointers.current.size > 1 || disabled || (event.target as HTMLElement).closest("input, textarea, select, button, a")) {
      start.current = null;
      return;
    }
    start.current = { id: event.pointerId, x: event.clientX, y: event.clientY };
  };
  const onPointerMove = (event: PointerEvent<HTMLElement>) => {
    const state = start.current;
    if (!state || state.id !== event.pointerId) return;
    const dx = event.clientX - state.x;
    const dy = event.clientY - state.y;
    // Clearly vertical: a scroll, not a swipe.
    if (Math.abs(dy) > 12 && Math.abs(dy) > Math.abs(dx)) start.current = null;
  };
  const onPointerEnd = (event: PointerEvent<HTMLElement>) => {
    pointers.current.delete(event.pointerId);
    const state = start.current;
    if (!state || state.id !== event.pointerId) return;
    start.current = null;
    if (event.type === "pointercancel") return;
    const dx = event.clientX - state.x;
    const dy = event.clientY - state.y;
    if (Math.abs(dx) < swipeThreshold || Math.abs(dx) <= Math.abs(dy)) return;
    onStep(dx < 0 ? 1 : -1);
  };

  return {
    handlers: {
      onPointerDown,
      onPointerMove,
      onPointerUp: onPointerEnd,
      onPointerCancel: onPointerEnd
    }
  };
}
