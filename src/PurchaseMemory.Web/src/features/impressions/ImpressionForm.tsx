import { IonButton, IonItem, IonLabel, IonList, IonNote, IonTextarea } from "@ionic/react";
import { useId, useRef, useState, type FormEvent, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { ApiError } from "../../api/client";
import type { ImpressionRating } from "../../api/types";
import { ActionButton, Footnote } from "../../design-system/components";
import { RatingControl } from "./RatingControl";

export interface ImpressionDraft {
  rating: ImpressionRating;
  note: string;
  photo: File | null;
}

export interface ImpressionFormProps {
  onSubmit: (draft: ImpressionDraft) => Promise<unknown>;
  onCancel?: () => void;
  /** Extra rows at the top of the form's list, e.g. which purchase is being rated. */
  children?: ReactNode;
  /** Disables saving, e.g. while there is nothing to attach the impression to. */
  disabled?: boolean;
}

export const maximumNoteLength = 2000;

/** Rating (required), note and photo (both optional). Every save adds a new entry to the history. */
export function ImpressionForm({ onSubmit, onCancel, children, disabled = false }: ImpressionFormProps) {
  const { t } = useTranslation();
  const photoId = useId();
  const photoInput = useRef<HTMLInputElement>(null);
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
    <form onSubmit={(event) => void submit(event)} noValidate>
      <IonList inset>
        {children}
        <IonItem>
          <RatingControl legend={t("impressions.ratingLegend")} value={rating} onChange={(value) => { setRating(value); setSaved(false); }} disabled={saving || disabled} />
        </IonItem>
        <IonItem>
          <IonTextarea
            label={t("impressions.noteLabel")}
            labelPlacement="stacked"
            autoGrow
            value={note}
            maxlength={maximumNoteLength}
            disabled={saving || disabled}
            onIonInput={(event) => setNote(String(event.detail.value ?? ""))}
          />
        </IonItem>
        <IonItem button detail={false} disabled={saving || disabled} onClick={() => photoInput.current?.click()} lines="none">
          <IonLabel className="pm-wrap" aria-hidden="true">{t("impressions.photoLabel")}</IonLabel>
          <IonNote slot="end" className="pm-pick-value">{photo?.name ?? t("impressions.choosePhoto")}</IonNote>
        </IonItem>
      </IonList>
      <input
        key={formKey}
        ref={photoInput}
        id={photoId}
        className="visually-hidden"
        type="file"
        accept="image/jpeg,image/png,image/webp"
        aria-label={t("impressions.photoLabel")}
        tabIndex={-1}
        onChange={(event) => setPhoto(event.target.files?.[0] ?? null)}
        disabled={saving || disabled}
      />
      {error && <Footnote tone="danger" role="alert">{error}</Footnote>}
      {saved && <Footnote tone="success" role="status">{t("impressions.saved")}</Footnote>}
      <div className="pm-primary-block">
        <ActionButton type="submit" expand="block" loading={saving} disabled={disabled}>{t("impressions.save")}</ActionButton>
        {onCancel && <IonButton expand="block" fill="clear" onClick={onCancel} disabled={saving}>{t("impressions.cancel")}</IonButton>}
      </div>
    </form>
  );
}
