import { IonAlert } from "@ionic/react";
import { AlertTriangle, Check, ChevronDown, ChevronLeft, ChevronRight, Tag } from "lucide-react";
import { useId, useState, type FocusEvent, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { parseDecimal } from "../../design-system/components";
import { formatMoney, formatQuantity } from "../purchases/money";
import { LinePhoto, type LineViewerPhoto } from "../receipt-photo/LinePhoto";
import { hasLocation } from "../receipt-photo/photoBox";
import { isDiscount } from "../receipts/discountLines";
import type { EditableLine } from "../receipts/LinesEditor";
import { carouselSteps, stepIndexOf, type CarouselStep } from "./carouselSteps";
import { lineProblem, totalsCheck, type LineProblem } from "./lineChecks";
import { useSwipe } from "./useSwipe";

/** At most this many dots (24 px each, fits a 320 px screen); with more cards they slide along with the current one. */
const maxDots = 8;

export interface LineCarouselProps<T extends EditableLine> {
  lines: T[];
  /** The line shown (controlled, so the screen can remember it and guard leaving it); a discount inside a product's card shows that card. */
  index: number;
  /** Called with the line of the card moved to (a product, or a discount with a card of its own). */
  onIndexChange: (index: number) => void;
  /** A field of a line on the card changed (the product or one of its discounts). */
  onChange: (index: number, patch: Partial<EditableLine>) => void;
  /** Called after the person confirmed «Удалить» (or «Убрать» on a discount). Omitted: no delete button. */
  onRemove?: (index: number) => void | Promise<void>;
  /**
   * Puts a discount removed with «Убрать» back where it was. Given, «Убрать»
   * removes at once and offers «Вернуть» while the card is shown; omitted, it
   * asks first (a saved purchase, where removing is saved at once).
   */
  onRestore?: (index: number, line: T) => void;
  isChecked: (index: number) => boolean;
  /**
   * «Верно, дальше»: marks the card's line and `related` (the discounts in its
   * card); the carousel then moves on unless this rejects.
   */
  onApprove: (index: number, related: number[]) => void | Promise<void>;
  /** Tapping «Проверено» takes the mark back (from `related` too). Omitted: the mark is just shown. */
  onUnapprove?: (index: number, related: number[]) => void;
  /** After the last card: given, its button reads «Верно, к итогу» and leads here (the receipt's summary). */
  onFinish?: () => void;
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
 * The receipt's products one at a time: the photo moves to the shown product
 * and highlights it, under it whether the lines add up, the product's
 * position with a dot per card, and the product itself, editable in place,
 * with the discounts that reduce it inside its card («Скидка −… · к оплате
 * …»). «Верно, дальше» marks it and moves on; a swipe (on the photo or the
 * card), the chevrons or a dot move freely. A discount on no product has a
 * card of its own (see `carouselSteps`). Used by the receipt draft and the
 * saved purchase.
 */
export function LineCarousel<T extends EditableLine>({
  lines,
  index,
  onIndexChange,
  onChange,
  onRemove,
  onRestore,
  isChecked,
  onApprove,
  onUnapprove,
  onFinish,
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
  // The line «Удалить» (or, in a saved purchase, a discount's «Убрать») asks about.
  const [confirmingRemove, setConfirmingRemove] = useState<{ position: number; discountRow: boolean } | null>(null);
  // A discount just taken out with «Убрать», offered back while its product is shown.
  const [removed, setRemoved] = useState<{ product: number; position: number; line: T } | null>(null);
  // The discount whose fields are open in the card.
  const [openDiscount, setOpenDiscount] = useState<number | null>(null);
  // Moves are animated only once the person has moved: the first layout (the photo measuring itself) is not.
  const [moved, setMoved] = useState(false);
  const regionId = useId();

  // While a field of the card is being typed in, the card keeps its grouping:
  // a discount briefly larger than its product (a sum being retyped) would
  // otherwise jump out of the card and take the focus with it. Moving,
  // removing or adding a line ends it.
  const [frozen, setFrozen] = useState<{ steps: CarouselStep[]; count: number } | null>(null);
  const computedSteps = carouselSteps(lines);
  const steps = frozen && frozen.count === lines.length ? frozen.steps : computedSteps;
  const count = steps.length;
  const current = count === 0 ? 0 : stepIndexOf(steps, index);
  const go = (next: number) => {
    if (busy || next < 0 || next >= count || next === current) return;
    setZoomed(false);
    setMoved(true);
    setRemoved(null);
    setOpenDiscount(null);
    setFrozen(null);
    onIndexChange(steps[next].position);
  };
  // A zoomed photo pans under one finger, so only the card swipes then.
  const photoSwipe = useSwipe((step) => go(current + step), zoomed);
  const cardSwipe = useSwipe((step) => go(current + step), false);

  const step = steps[current];
  const line = step ? lines[step.position] : undefined;
  if (!step || !line) return null;
  const last = current + 1 >= count;
  const advance = () => { if (!last) go(current + 1); else onFinish?.(); };
  const approve = () => {
    if (busy) return;
    const result = onApprove(step.position, step.discounts);
    // A rejected approval (a correction that could not be saved) keeps the line; the screen shows why.
    if (result instanceof Promise) result.then(advance, () => undefined);
    else advance();
  };
  const removeDiscount = (position: number) => {
    if (busy) return;
    if (!onRestore) { setConfirmingRemove({ position, discountRow: true }); return; }
    setRemoved({ product: step.position, position, line: lines[position] });
    setOpenDiscount(null);
    void onRemove?.(position);
  };
  const restoreDiscount = () => {
    if (!removed || !onRestore) return;
    onRestore(removed.position, removed.line);
    setRemoved(null);
  };

  const problem = lineProblem(line);
  const checked = isChecked(step.position);
  const discount = step.kind === "discount";
  const name = line.name.trim() || t("lines.untitled");
  const productCount = steps.filter((other) => other.kind === "product").length;
  const productNumber = steps.slice(0, current + 1).filter((other) => other.kind === "product").length;
  const positionText = stepText(t, step, productNumber, productCount);
  const discountNote = !discount ? null : step.appliesToReceipt ? t("lineReview.receiptDiscount") : t("discount.unlinked");
  // What is left to pay for the product after its discounts (on the last discount row).
  const payable = line.lineTotal === null || step.discounts.length === 0
    ? null
    : Math.round((line.lineTotal + step.discounts.reduce((sum, position) => sum + (lines[position].lineTotal ?? 0), 0)) * 100) / 100;
  const removedHere = removed !== null && removed.product === step.position && onRestore !== undefined;
  const removingLine = confirmingRemove === null ? null : lines[confirmingRemove.position];
  const confirmRemove = () => {
    if (confirmingRemove === null) return;
    // Its undo belonged to the card as it was; after another removal it could put the discount by the wrong product.
    setRemoved(null);
    setFrozen(null);
    void onRemove?.(confirmingRemove.position);
  };
  // Typing in the card's fields keeps its grouping (see `frozen`).
  const cardFocus = {
    onFocus: (event: FocusEvent<HTMLDivElement>) => {
      if (event.target instanceof HTMLInputElement && frozen === null) setFrozen({ steps, count: lines.length });
    },
    onBlur: (event: FocusEvent<HTMLDivElement>) => {
      const next = event.relatedTarget;
      if (!(next instanceof HTMLInputElement && event.currentTarget.contains(next))) setFrozen(null);
    }
  };
  const finishing = last && onFinish !== undefined;

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
          <span className="pm-carousel-count">{positionText}</span>
          <Dots lines={lines} steps={steps} current={current} isChecked={isChecked} onGo={go} />
        </div>
        <button type="button" className="pm-carousel-chevron" onClick={() => go(current + 1)} disabled={last || busy} aria-label={t("lineCarousel.next")}>
          <ChevronRight size={24} aria-hidden="true" />
        </button>
      </div>

      <div className="pm-carousel-card" key={current} data-animate={moved} {...cardSwipe.handlers} {...cardFocus} data-testid="line-carousel-card">
        {problem && (
          <p className="pm-carousel-problem">
            <AlertTriangle size={16} aria-hidden="true" />
            <span>{problemText(t, problem, line, money, locale)}</span>
          </p>
        )}
        {discountNote && <p className="pm-carousel-note">{t("discount.label")} {discountNote}</p>}
        <TextField
          className="pm-carousel-name"
          label={discount ? t("discount.name") : t("purchaseEdit.name")}
          value={line.name}
          readOnly={busy}
          onChange={(value) => onChange(step.position, { name: value })}
        />
        {discount
          ? (
            <div className="pm-carousel-figures pm-carousel-figures-single">
              <NumberField
                readOnly={busy}
                label={t("discount.size")}
                value={line.lineTotal === null ? null : Math.abs(line.lineTotal)}
                invalid={line.lineTotal === null}
                onChange={(value) => onChange(step.position, { lineTotal: value === null ? null : -Math.abs(value) })}
              />
            </div>
          )
          : (
            <div className="pm-carousel-figures">
              <NumberField
                readOnly={busy}
                label={line.unit ? t("lineCarousel.quantityUnit", { unit: line.unit }) : t("lineCarousel.quantity")}
                value={line.quantity}
                onChange={(quantity) => onChange(step.position, { quantity })}
              />
              <NumberField readOnly={busy} label={t("legacyCapture.price")} value={line.unitPrice} invalid={line.unitPrice === null} onChange={(unitPrice) => onChange(step.position, { unitPrice })} />
              <NumberField readOnly={busy} label={t("legacyCapture.amount")} value={line.lineTotal} invalid={line.lineTotal === null || problem?.kind === "arithmetic"} onChange={(lineTotal) => onChange(step.position, { lineTotal })} />
            </div>
          )}
        {step.discounts.map((position, offset) => (
          <DiscountRow
            key={position}
            line={lines[position]}
            payable={offset === step.discounts.length - 1 ? payable : null}
            // A discount without its amount opens by itself: that is what needs filling in.
            open={openDiscount === position || lineProblem(lines[position]) !== null}
            onToggle={() => setOpenDiscount((value) => value === position ? null : position)}
            onRemove={onRemove ? () => removeDiscount(position) : undefined}
            onChange={(patch) => onChange(position, patch)}
            busy={busy}
            money={money}
          />
        ))}
        {removedHere && (
          <div className="pm-carousel-discount pm-carousel-discount-removed">
            <span className="pm-carousel-discount-text" role="status">{t("lineCarousel.discountRemoved")}</span>
            <button type="button" className="pm-carousel-discount-action" onClick={restoreDiscount}>{t("lineCarousel.discountRestore")}</button>
          </div>
        )}
      </div>

      {error && <div className="pm-carousel-error" role="alert">{error}</div>}

      <div className="pm-carousel-actions">
        {onRemove && (
          <button type="button" className="pm-carousel-remove" disabled={busy || lines.length <= 1} onClick={() => setConfirmingRemove({ position: step.position, discountRow: false })}>
            {t("ui.delete")}
          </button>
        )}
        {checked
          ? onFinish
            ? (
              // The draft's own flow: a checked card still leads on, and its mark comes off with the small ✓.
              <>
                {onUnapprove && (
                  <button type="button" className="pm-carousel-checked pm-carousel-checked-compact" disabled={busy} onClick={() => onUnapprove(step.position, step.discounts)} aria-label={t("lineReview.unapprove")} title={t("lineReview.checked")}>
                    <Check size={18} aria-hidden="true" />
                  </button>
                )}
                <button type="button" className="pm-carousel-approve" disabled={busy} onClick={advance}>
                  {finishing ? t("lineCarousel.toSummary") : t("lineCarousel.onward")}
                  <ChevronRight size={18} aria-hidden="true" />
                </button>
              </>
            )
            : onUnapprove
              ? (
                <button type="button" className="pm-carousel-checked" disabled={busy} onClick={() => onUnapprove(step.position, step.discounts)} aria-label={t("lineReview.unapprove")}>
                  <Check size={18} aria-hidden="true" />
                  <span aria-hidden="true">{t("lineReview.checked")}</span>
                </button>
              )
              : <span className="pm-carousel-checked"><Check size={18} aria-hidden="true" />{t("lineReview.checked")}</span>
          : (
            <button type="button" className="pm-carousel-approve" disabled={busy} onClick={approve}>
              <Check size={18} aria-hidden="true" />
              {!last ? t("lineCarousel.approveNext") : finishing ? t("lineCarousel.approveFinish") : t("lineCarousel.approveLast")}
            </button>
          )}
      </div>

      {extra}

      <span className="visually-hidden" role="status" aria-live="polite">
        {t("lineCarousel.announcement", { position: positionText, name, amount: money(line.lineTotal) })}
      </span>

      <IonAlert
        isOpen={removingLine !== null && removingLine !== undefined}
        header={confirmingRemove?.discountRow
          ? t("lineCarousel.discountRemoveTitle")
          : removingLine && isDiscount({ kind: removingLine.kind ?? "product" })
            ? t("lineCarousel.removeDiscountTitle")
            : t("lineCarousel.removeTitle", { number: productNumber })}
        message={removingLine ? removingLine.name.trim() || t("lines.untitled") : ""}
        buttons={[
          { text: t("ui.cancel"), role: "cancel" },
          { text: confirmingRemove?.discountRow ? t("lineCarousel.discountRemove") : t("ui.delete"), role: "destructive", handler: confirmRemove }
        ]}
        onDidDismiss={() => setConfirmingRemove(null)}
      />
    </section>
  );
}

/** «Товар 2 из 5», or what kind of discount a discount's own card is. */
function stepText(t: ReturnType<typeof useTranslation>["t"], step: CarouselStep, number: number, count: number): string {
  if (step.kind === "product") return t("lineCarousel.position", { number, count });
  return step.appliesToReceipt ? t("lineCarousel.receiptDiscountPosition") : t("lineCarousel.discountPosition");
}

/**
 * A discount inside its product's card: «Скидка −7,86 · к оплате 36,70» and
 * «Убрать». Tapping the text opens its name and size for editing.
 */
function DiscountRow({ line, payable, open, onToggle, onRemove, onChange, busy, money }: {
  line: EditableLine;
  payable: number | null;
  open: boolean;
  onToggle: () => void;
  onRemove?: () => void;
  onChange: (patch: Partial<EditableLine>) => void;
  busy: boolean;
  money: (value: number | null) => string;
}) {
  const { t } = useTranslation();
  const fieldsId = useId();
  const amount = line.lineTotal === null ? null : Math.abs(line.lineTotal);
  const amountText = amount === null ? t("lineCarousel.discountUnknown") : t("lineCarousel.discountAmount", { amount: money(amount) });
  return (
    <div className="pm-carousel-discount-block" data-problem={amount === null || undefined}>
      <div className="pm-carousel-discount">
        <Tag size={18} aria-hidden="true" className="pm-carousel-discount-icon" />
        <button type="button" className="pm-carousel-discount-text" aria-expanded={open} aria-controls={open ? fieldsId : undefined} onClick={onToggle}>
          <span>
            {amountText}
            {payable !== null && <>{" · "}{t("lineCarousel.payable")} <b>{money(payable)}</b></>}
          </span>
          <ChevronDown size={16} aria-hidden="true" className="pm-carousel-discount-chevron" />
        </button>
        {onRemove && (
          <button
            type="button"
            className="pm-carousel-discount-action"
            disabled={busy}
            aria-label={t("lineCarousel.discountRemoveLabel", { name: line.name.trim() || t("lines.untitled") })}
            onClick={onRemove}
          >
            {t("lineCarousel.discountRemove")}
          </button>
        )}
      </div>
      {open && (
        <div id={fieldsId} className="pm-carousel-figures pm-carousel-figures-two">
          <TextField label={t("discount.name")} value={line.name} readOnly={busy} onChange={(name) => onChange({ name })} />
          <NumberField
            readOnly={busy}
            label={t("discount.size")}
            value={amount}
            invalid={amount === null}
            onChange={(value) => onChange({ lineTotal: value === null ? null : -Math.abs(value) })}
          />
        </div>
      )}
    </div>
  );
}

function TextField({ label, value, onChange, readOnly, className = "" }: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  readOnly: boolean;
  className?: string;
}) {
  const { t } = useTranslation();
  return (
    <label className={["pm-carousel-field", className].filter(Boolean).join(" ")}>
      <span>{label}</span>
      <input
        type="text"
        value={value}
        placeholder={t("lines.namePlaceholder")}
        aria-invalid={value.trim() ? undefined : true}
        enterKeyHint="done"
        // While a correction is being saved the fields wait, so nothing typed meanwhile is lost.
        readOnly={readOnly}
        onChange={(event) => onChange(event.target.value)}
      />
    </label>
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

function Dots({ lines, steps, current, isChecked, onGo }: {
  lines: EditableLine[];
  steps: CarouselStep[];
  current: number;
  isChecked: (index: number) => boolean;
  onGo: (index: number) => void;
}) {
  const { t } = useTranslation();
  const first = Math.max(0, Math.min(current - Math.floor(maxDots / 2), steps.length - maxDots));
  const shown = steps.slice(first, first + maxDots);
  return (
    <div className="pm-carousel-dots">
      {shown.map((step, offset) => {
        const position = first + offset;
        const problem = [step.position, ...step.discounts].some((line) => lineProblem(lines[line]) !== null);
        const checked = isChecked(step.position);
        // A line the person confirmed counts as checked even if its figures still look odd.
        const state = position === current ? "current" : checked ? "checked" : problem ? "problem" : "plain";
        const suffix = checked
          ? step.kind === "product" ? t("lineCarousel.dotChecked") : t("lineCarousel.dotCheckedDiscount")
          : problem ? t("lineCarousel.dotProblem") : "";
        const number = steps.slice(0, position + 1).filter((other) => other.kind === "product").length;
        const label = step.kind === "product" ? t("lineCarousel.productDot", { number }) : t("lineCarousel.discountPosition");
        return (
          <button
            key={step.position}
            type="button"
            className="pm-carousel-dot"
            data-state={state}
            aria-current={position === current ? "true" : undefined}
            aria-label={`${label}${suffix}`}
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
