import { useId, useState, type FormEvent, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { ApiError } from "../../api/client";
import type { ImpressionRating } from "../../api/types";
import { Button } from "../../design-system/components";
import { RatingControl } from "./RatingControl";
import styles from "./Impressions.module.css";

export interface ImpressionDraft {
  rating: ImpressionRating;
  note: string;
  photo: File | null;
}

export interface ImpressionFormProps {
  onSubmit: (draft: ImpressionDraft) => Promise<unknown>;
  onCancel?: () => void;
  /** Extra fields above the rating, e.g. which purchase is being rated. */
  children?: ReactNode;
  /** Disables saving, e.g. while there is nothing to attach the impression to. */
  disabled?: boolean;
}

export const maximumNoteLength = 2000;

/** Rating (required), note and photo (both optional). Every save adds a new entry to the history. */
export function ImpressionForm({ onSubmit, onCancel, children, disabled = false }: ImpressionFormProps) {
  const { t } = useTranslation();
  const noteId = useId();
  const photoId = useId();
  const [rating, setRating] = useState<ImpressionRating | null>(null);
  const [note, setNote] = useState("");
  const [photo, setPhoto] = useState<File | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [formKey, setFormKey] = useState(0);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!rating) {
      setError(t("impressions.ratingRequired"));
      return;
    }
    setSaving(true);
    setError(null);
    setSaved(false);
    try {
      await onSubmit({ rating, note, photo });
      setRating(null);
      setNote("");
      setPhoto(null);
      setFormKey((key) => key + 1); // clears the file input
      setSaved(true);
    } catch (caught) {
      setError(caught instanceof ApiError && caught.status === 400 ? t("impressions.invalid", { detail: caught.message }) : t("impressions.saveFailed"));
    } finally {
      setSaving(false);
    }
  };

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} noValidate>
      {children}
      <RatingControl legend={t("impressions.ratingLegend")} value={rating} onChange={(value) => { setRating(value); setSaved(false); }} disabled={saving || disabled} />
      <label htmlFor={noteId}>
        {t("impressions.noteLabel")}
        <textarea
          id={noteId}
          value={note}
          maxLength={maximumNoteLength}
          onChange={(event) => setNote(event.target.value)}
          disabled={saving || disabled}
        />
      </label>
      <label htmlFor={photoId}>
        {t("impressions.photoLabel")}
        <input
          key={formKey}
          id={photoId}
          type="file"
          accept="image/jpeg,image/png,image/webp"
          onChange={(event) => setPhoto(event.target.files?.[0] ?? null)}
          disabled={saving || disabled}
        />
      </label>
      {error && <p className={styles.error} role="alert">{error}</p>}
      {saved && <p className={styles.success} role="status">{t("impressions.saved")}</p>}
      <div className={styles.formActions}>
        <Button type="submit" loading={saving} disabled={disabled}>{t("impressions.save")}</Button>
        {onCancel && <Button intent="quiet" onClick={onCancel} disabled={saving}>{t("impressions.cancel")}</Button>}
      </div>
    </form>
  );
}
