import { IonInput } from "@ionic/react";
import { useState, type Ref } from "react";
import { useTranslation } from "react-i18next";

export interface DecimalInputProps {
  label: string;
  value: number | null;
  onChange: (value: number | null) => void;
  /** `fixed` puts the label left of the value, as in iOS forms; `stacked` above it. */
  labelPlacement?: "fixed" | "stacked" | "start";
  placeholder?: string;
  invalid?: boolean;
  disabled?: boolean;
  id?: string;
  ariaLabel?: string;
  /** For `setFocus()`, e.g. when a review-queue link points at this field. */
  inputRef?: Ref<HTMLIonInputElement>;
}

/** «12,5» and «12.5» are both 12.5; anything that is not a number yet is not sent. */
export function parseDecimal(text: string): number | null | undefined {
  const normalized = text.trim().replace(/\s/g, "").replace(",", ".");
  if (normalized === "") return null;
  if (!/^-?\d*\.?\d*$/.test(normalized) || normalized === "." || normalized === "-") return undefined;
  const value = Number(normalized);
  return Number.isFinite(value) ? value : undefined;
}

function formatDecimal(value: number | null, comma: boolean): string {
  if (value === null) return "";
  const text = String(value);
  return comma ? text.replace(".", ",") : text;
}

/**
 * An amount or quantity typed on the iPhone decimal keypad. The keypad shows
 * a comma in Russian and Polish; a native `type="number"` field rejects that
 * comma in some iOS versions, so this is a text field that understands both
 * separators. While the text is not a number yet (a lone «,»), nothing is
 * reported, and the field keeps what was typed.
 */
export function DecimalInput({ label, value, onChange, labelPlacement = "fixed", placeholder, invalid = false, disabled = false, id, ariaLabel, inputRef }: DecimalInputProps) {
  const { i18n } = useTranslation();
  const comma = i18n.resolvedLanguage !== "en";
  const [text, setText] = useState(() => formatDecimal(value, comma));
  // A value changed from outside (a «use the lines total» button) replaces the text.
  const [shown, setShown] = useState(value);
  if (value !== shown) {
    setShown(value);
    if (parseDecimal(text) !== value) setText(formatDecimal(value, comma));
  }

  return (
    <IonInput
      ref={inputRef}
      id={id}
      label={label}
      labelPlacement={labelPlacement}
      aria-label={ariaLabel}
      inputmode="decimal"
      enterkeyhint="done"
      value={text}
      placeholder={placeholder}
      disabled={disabled}
      className={invalid ? "ion-invalid ion-touched" : undefined}
      aria-invalid={invalid || undefined}
      onIonInput={(event) => {
        const next = String(event.detail.value ?? "");
        setText(next);
        const parsed = parseDecimal(next);
        if (parsed !== undefined) {
          setShown(parsed);
          onChange(parsed);
        }
      }}
    />
  );
}
