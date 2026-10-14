import { IonButton, IonItem, IonLabel, IonList, IonNote } from "@ionic/react";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { useEffect, useRef, useState, type KeyboardEvent, type PointerEvent } from "react";
import { useTranslation } from "react-i18next";
import type { BoundingBox, ReceiptLineKind } from "../../api/types";
import { Footnote, Notice, Sheet } from "../../design-system/components";
import { formatMoney, formatQuantity } from "../purchases/money";
import { useLineLocatorMode, type LineLocatorMode } from "./lineLocatorMode";
import type { ReceiptPhoto } from "./photoBox";
import { canHighlight, ReceiptPhotoFocus } from "./ReceiptPhotoFocus";

/** What the viewer needs from a receipt or purchase line. */
export interface LocatableLine {
  name: string;
  quantity: number | null;
  unit: string | null;
  unitPrice: number | null;
  lineTotal: number | null;
  kind?: ReceiptLineKind;
  boundingBox?: BoundingBox | null;
}

/** The receipt photo as a screen has it: still loading, failed, or ready. */
export interface LineViewerPhoto {
  photo: ReceiptPhoto | null;
  failed: boolean;
}

export interface ReceiptLineViewerProps {
  lines: LocatableLine[];
  /** The line to open at; `null` keeps the viewer closed. */
  openIndex: number | null;
  onClose: () => void;
  photo: LineViewerPhoto;
  currency: string | null;
  /** Overrides the person's setting (stories); screens leave it out. */
  mode?: LineLocatorMode;
}

/**
 * Shows where receipt lines are on the receipt photo, as a sheet over the
 * screen. In `tap` mode it shows the one line that was tapped; in `swipe`
 * mode it starts there and moves line by line with a swipe on the photo, the
 * «Предыдущая»/«Следующая» buttons or the arrow keys.
 */
export function ReceiptLineViewer({ lines, openIndex, onClose, photo, currency, mode: modeOverride }: ReceiptLineViewerProps) {
  const { t } = useTranslation();
  const [storedMode] = useLineLocatorMode();
  const mode = modeOverride ?? storedMode;
  const isOpen = openIndex !== null && openIndex >= 0 && openIndex < lines.length;
  const requested = isOpen ? openIndex : null;
  // The last opened line stays rendered after closing, so the sheet keeps its
  // content (and title) during the dismiss animation. Every opening is a new
  // session, so a new tap starts again from the tapped line.
  const [shown, setShown] = useState<{ index: number; session: number } | null>(null);
  const [lastRequested, setLastRequested] = useState<number | null>(null);
  if (requested !== lastRequested) {
    setLastRequested(requested);
    if (requested !== null) setShown((current) => ({ index: requested, session: (current?.session ?? 0) + 1 }));
  }
  const body = shown !== null && shown.index < lines.length ? shown : null;

  return (
    <Sheet
      isOpen={isOpen}
      onClose={onClose}
      title={mode === "swipe" ? t("lineLocator.titleSwipe") : t("lineLocator.titleOne", { number: (body?.index ?? 0) + 1 })}
      initialBreakpoint={0.85}
    >
      {body && (
        <ViewerBody
          key={body.session}
          lines={lines}
          startIndex={body.index}
          navigable={mode === "swipe"}
          photo={photo}
          currency={currency}
        />
      )}
    </Sheet>
  );
}

const swipeThreshold = 40;

function ViewerBody({ lines, startIndex, navigable, photo, currency }: {
  lines: LocatableLine[];
  startIndex: number;
  navigable: boolean;
  photo: LineViewerPhoto;
  currency: string | null;
}) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage === "en" ? "en-GB" : "ru-RU";
  const [index, setIndex] = useState(startIndex);
  const [photoRatio, setPhotoRatio] = useState<number | null>(null);
  const pointerStart = useRef<{ x: number; y: number } | null>(null);
  const navRef = useRef<HTMLDivElement>(null);
  const statusRef = useRef<HTMLSpanElement>(null);
  const keepFocus = useRef(false);
  const line = lines[Math.min(index, lines.length - 1)];
  const name = line.name.trim() || t("lines.untitled");
  const money = (value: number | null) => value === null ? "—" : formatMoney(value, currency, locale);
  const hasPrevious = navigable && index > 0;
  const hasNext = navigable && index < lines.length - 1;

  const go = (next: number) => {
    if (next < 0 || next >= lines.length) return;
    // At either end the pressed button becomes disabled and would drop focus
    // out of the sheet; the status takes it instead.
    const atEnd = next === 0 || next === lines.length - 1;
    keepFocus.current = atEnd && navRef.current?.contains(document.activeElement) === true;
    setIndex(next);
  };

  useEffect(() => {
    if (!keepFocus.current) return;
    keepFocus.current = false;
    statusRef.current?.focus();
  }, [index]);

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (!navigable) return;
    if (event.key === "ArrowLeft") { event.preventDefault(); go(index - 1); }
    if (event.key === "ArrowRight") { event.preventDefault(); go(index + 1); }
  };

  const onPointerDown = (event: PointerEvent<HTMLDivElement>) => {
    pointerStart.current = { x: event.clientX, y: event.clientY };
  };
  const onPointerUp = (event: PointerEvent<HTMLDivElement>) => {
    const start = pointerStart.current;
    pointerStart.current = null;
    if (!start) return;
    const dx = event.clientX - start.x;
    const dy = event.clientY - start.y;
    // Mostly horizontal and long enough: a swipe, not a scroll or a tap.
    if (Math.abs(dx) < swipeThreshold || Math.abs(dx) < Math.abs(dy) * 1.5) return;
    go(dx < 0 ? index + 1 : index - 1);
  };

  const located = photo.photo !== null && canHighlight(photo.photo, line.boundingBox);
  const note = photo.photo === null
    ? null
    : !photo.photo.croppable
      ? t("lineLocator.multiPart")
      : located ? null : t("lineLocator.noBox");

  return (
    <div className="pm-locator" onKeyDown={onKeyDown}>
      <div
        className="pm-locator-stage"
        data-swipe={navigable}
        onPointerDown={navigable ? onPointerDown : undefined}
        onPointerUp={navigable ? onPointerUp : undefined}
        onPointerCancel={() => { pointerStart.current = null; }}
      >
        {photo.photo
          ? (
            <ReceiptPhotoFocus
              photo={photo.photo}
              box={line.boundingBox}
              focusAlt={t("lineLocator.photoLineAlt", { name })}
              wholeAlt={t("lineLocator.photoWholeAlt")}
              photoRatio={photoRatio}
              onPhotoRatio={setPhotoRatio}
            />
          )
          : photo.failed
            ? <Notice tone="danger" role="alert"><p>{t("lineLocator.photoFailed")}</p></Notice>
            : <Footnote>{t("lineLocator.photoLoading")}</Footnote>}
      </div>
      {note && <Footnote>{note}</Footnote>}

      <IonList inset>
        <IonItem lines="none">
          <IonLabel className="pm-wrap">
            {line.kind === "discount" && <p className="pm-overline">{t("discount.label")}</p>}
            <h3>{name}</h3>
            {line.kind !== "discount" && (
              <p>{line.quantity === null ? "—" : formatQuantity(line.quantity, locale)}{line.unit ? ` ${line.unit}` : ""} × {money(line.unitPrice)}</p>
            )}
          </IonLabel>
          <IonNote slot="end" className="pm-amount">{money(line.lineTotal)}</IonNote>
        </IonItem>
      </IonList>

      {navigable && (
        <>
          <div className="pm-locator-nav" ref={navRef}>
            <IonButton fill="clear" disabled={!hasPrevious} onClick={() => go(index - 1)} aria-label={t("lineLocator.previousLabel")}>
              <ChevronLeft aria-hidden="true" size={22} />
              <span>{t("lineLocator.previous")}</span>
            </IonButton>
            <span className="pm-locator-position" ref={statusRef} tabIndex={-1}>
              <span aria-hidden="true">{t("lineLocator.position", { number: index + 1, count: lines.length })}</span>
              <span className="visually-hidden" role="status" aria-live="polite">
                {t("lineLocator.positionAnnouncement", { number: index + 1, count: lines.length, name, amount: money(line.lineTotal) })}
              </span>
            </span>
            <IonButton fill="clear" disabled={!hasNext} onClick={() => go(index + 1)} aria-label={t("lineLocator.nextLabel")}>
              <span>{t("lineLocator.next")}</span>
              <ChevronRight aria-hidden="true" size={22} />
            </IonButton>
          </div>
          <Footnote>{t("lineLocator.swipeHint")}</Footnote>
        </>
      )}
    </div>
  );
}
