import type { InputHTMLAttributes } from "react";
import { useTranslation } from "react-i18next";
import styles from "./DateInput.module.css";

export interface DateInputProps extends Omit<InputHTMLAttributes<HTMLInputElement>, "type" | "value"> {
  /** `date` (default) or `datetime-local`; both get the same iPhone treatment. */
  type?: "date" | "datetime-local";
  /** `yyyy-mm-dd` (or `yyyy-mm-ddThh:mm`), or `""` while nothing is chosen. */
  value: string;
  /** Text shown while empty; defaults to «Любая дата». */
  emptyText?: string;
}

/**
 * A native date (or date-and-time) field that behaves inside narrow cards on iPhone.
 *
 * iOS Safari gives `<input type="date">` its own intrinsic minimum width and
 * button-like appearance, so two of them side by side overflow a 390 px card,
 * and an empty one is a blank box with no hint. This resets the appearance,
 * lets the field shrink with its column and, where the browser draws nothing
 * for an empty value (iOS), lays a muted «Любая дата» over it. Desktop
 * browsers keep their own «дд.мм.гггг» hint. The native picker is unchanged.
 */
export function DateInput({ type = "date", value, emptyText, className, ...props }: DateInputProps) {
  const { t } = useTranslation();
  const empty = value === "";

  return (
    // The hint is CSS generated content rather than a text node, so it never
    // becomes part of the surrounding <label>'s text.
    <span className={styles.root} data-empty-text={empty ? (emptyText ?? t("common.dateEmpty")) : undefined}>
      <input {...props} type={type} value={value} className={[styles.input, className].filter(Boolean).join(" ")} />
    </span>
  );
}
