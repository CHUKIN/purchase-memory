import { IonButton, IonButtons, IonDatetime, IonItem, IonLabel, IonModal, IonNote, IonTitle, IonToolbar } from "@ionic/react";
import { useId, useState } from "react";
import { useTranslation } from "react-i18next";

export interface DateFieldProps {
  label: string;
  /** `yyyy-mm-dd` (or `yyyy-mm-ddThh:mm` with `withTime`), or `""` while nothing is chosen. */
  value: string;
  onChange: (value: string) => void;
  /** Date and time instead of a calendar day. */
  withTime?: boolean;
  /** Shown while empty; defaults to «Любая дата». */
  emptyText?: string;
  /** Offer «Сбросить» to go back to empty. */
  clearable?: boolean;
  min?: string;
  max?: string;
  disabled?: boolean;
  id?: string;
  /** In a form of editable fields: the value in the text colour like the inputs around it, not grey. */
  formValue?: boolean;
}

/**
 * A date as an iOS list row: «Дата с … 1 авг. 2026 г.». Tapping it opens a
 * bottom sheet with Ionic's calendar (and time wheel); every change applies
 * at once, «Готово» closes and «Сбросить» empties the field.
 *
 * This replaces the old native `<input type="date">` wrapper, whose job was
 * to keep iOS date fields from overflowing narrow cards: the value is now
 * plain text on the right of a row, which never overflows.
 */
export function DateField({ label, value, onChange, withTime = false, emptyText, clearable = true, min, max, disabled = false, id, formValue = false }: DateFieldProps) {
  const { t, i18n } = useTranslation();
  const [open, setOpen] = useState(false);
  const titleId = useId();
  const locale = i18n.resolvedLanguage === "en" ? "en-GB" : "ru-RU";
  const text = value ? formatValue(value, withTime, locale) : (emptyText ?? t("common.dateEmpty"));

  return (
    <>
      <IonItem id={id} button detail={false} disabled={disabled} onClick={() => setOpen(true)}>
        <IonLabel>{label}</IonLabel>
        <IonNote slot="end" className={formValue ? "pm-pick-value pm-field-value" : "pm-pick-value"}>{text}</IonNote>
      </IonItem>
      <IonModal isOpen={open} onDidDismiss={() => setOpen(false)} className="pm-auto-height" initialBreakpoint={1} breakpoints={[0, 1]} aria-labelledby={titleId}>
        {/* An auto-height sheet must not use IonContent (it would collapse to nothing). */}
        <IonToolbar>
          {clearable && (
            <IonButtons slot="start">
              <IonButton disabled={!value} onClick={() => { onChange(""); setOpen(false); }}>{t("ui.reset")}</IonButton>
            </IonButtons>
          )}
          <IonTitle><h2 id={titleId} className="pm-title-heading">{label}</h2></IonTitle>
          <IonButtons slot="end"><IonButton strong onClick={() => setOpen(false)}>{t("ui.done")}</IonButton></IonButtons>
        </IonToolbar>
        <div className="pm-sheet-body">
          <IonDatetime
            aria-label={label}
            presentation={withTime ? "date-time" : "date"}
            locale={locale}
            firstDayOfWeek={1}
            hourCycle="h23"
            value={value || undefined}
            min={min || undefined}
            max={max || undefined}
            onIonChange={(event) => {
              const next = event.detail.value;
              if (typeof next === "string") onChange(next.slice(0, withTime ? 16 : 10));
            }}
          />
        </div>
      </IonModal>
    </>
  );
}

function formatValue(value: string, withTime: boolean, locale: string): string {
  // `yyyy-mm-dd` alone would be read as UTC midnight; the local noon keeps the day.
  const date = new Date(withTime ? value : `${value.slice(0, 10)}T12:00`);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat(locale, withTime ? { dateStyle: "medium", timeStyle: "short" } : { dateStyle: "medium" }).format(date);
}
