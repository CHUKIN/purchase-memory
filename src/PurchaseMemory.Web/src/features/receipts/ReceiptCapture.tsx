import { IonActionSheet, IonButton, IonItem, IonLabel, IonList, IonNote, IonRadio, IonRadioGroup } from "@ionic/react";
import { Camera, Images, RotateCcw } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { ButtonLink, Footnote, ItemLink, PrimaryButton, SectionHeader } from "../../design-system/components";
import { HelpLink } from "../help/HelpLink";
import type { BatchPhotoState } from "./useReceiptBatchUpload";

export const receiptCaptureLimits = { maxParts: 8, maxPartBytes: 15 * 1024 * 1024, maxTotalBytes: 60 * 1024 * 1024 } as const;
/**
 * Each separate receipt spends one token of the per-user upload limit
 * (a burst of 10, see `RateLimiting:ReceiptUploads`), so a batch never asks
 * for more than a full burst. A partly spent burst still ends in a 429,
 * which the batch handles by stopping.
 */
export const receiptBatchLimits = { maxPhotos: 10 } as const;

/** "parts": all photos are one receipt. "separate": every photo is its own receipt. */
export type CaptureMode = "parts" | "separate";

type Translate = (key: string, options?: Record<string, unknown>) => string;

export interface ReceiptCaptureProps {
  files: File[];
  uploading?: boolean;
  disabled?: boolean;
  error?: string | null;
  mode?: CaptureMode;
  /** Present the choice only when the screen can act on it. */
  onModeChange?: (mode: CaptureMode) => void;
  /** Per-photo progress of a started "separate receipts" batch. */
  batch?: BatchPhotoState[] | null;
  onRetryPhoto?: (index: number) => void;
  onStartOver?: () => void;
  onFilesChange: (files: File[]) => void;
  onSubmit: () => void;
}

/**
 * Picking the photos: «Сфотографировать» and «Выбрать из галереи» rows, the
 * chosen photos as a list (tap one to move or remove it), the «одного чека /
 * отдельные чеки» choice once there are several, and one upload button.
 */
export function ReceiptCapture({ files, uploading = false, disabled = false, error, mode = "parts", onModeChange, batch = null, onRetryPhoto, onStartOver, onFilesChange, onSubmit }: ReceiptCaptureProps) {
  const { t, i18n } = useTranslation();
  const cameraInput = useRef<HTMLInputElement>(null);
  const galleryInput = useRef<HTMLInputElement>(null);
  const [previews, setPreviews] = useState<string[]>([]);
  const [actionsFor, setActionsFor] = useState<number | null>(null);
  const locale = i18n.resolvedLanguage === "en" ? "en" : "ru";

  useEffect(() => {
    const urls = files.map((file) => URL.createObjectURL(file));
    setPreviews(urls);
    return () => urls.forEach((url) => URL.revokeObjectURL(url));
  }, [files]);

  // A single photo is one receipt either way; the choice only matters for several.
  const separate = batch !== null || (mode === "separate" && files.length > 1);
  const validation = useMemo(() => separate ? validateSeparate(files, t) : validateParts(files, t), [files, separate, t]);
  const started = batch !== null;
  function append(selected: FileList | null) {
    if (!selected) return;
    onFilesChange([...files, ...Array.from(selected)]);
  }
  function move(index: number, offset: number) {
    const next = [...files];
    const target = index + offset;
    [next[index], next[target]] = [next[target], next[index]];
    onFilesChange(next);
  }
  function remove(index: number) {
    onFilesChange(files.filter((_, position) => position !== index));
  }
  const name = (index: number) => separate ? t("receiptCapture.receipt", { number: index + 1 }) : t("receiptCapture.part", { number: index + 1 });

  return (
    <>
      {!started && (
        <>
          <input ref={cameraInput} className="visually-hidden" tabIndex={-1} aria-label={t("receiptCapture.camera")} type="file" accept="image/jpeg,image/png,image/webp" capture="environment" onChange={(event) => { append(event.target.files); event.target.value = ""; }} />
          <input ref={galleryInput} className="visually-hidden" tabIndex={-1} aria-label={t("receiptCapture.gallery")} type="file" accept="image/jpeg,image/png,image/webp" multiple onChange={(event) => { append(event.target.files); event.target.value = ""; }} />
          <IonList inset>
            <IonItem button detail={false} disabled={uploading} onClick={() => cameraInput.current?.click()}>
              <Camera slot="start" size={24} className="pm-row-icon" aria-hidden="true" />
              <IonLabel color="primary">{t("receiptCapture.camera")}</IonLabel>
            </IonItem>
            <IonItem button detail={false} lines="none" disabled={uploading} onClick={() => galleryInput.current?.click()}>
              <Images slot="start" size={24} className="pm-row-icon" aria-hidden="true" />
              <IonLabel color="primary">{t("receiptCapture.gallery")}</IonLabel>
            </IonItem>
          </IonList>
          <Footnote>{separate ? t("receiptCapture.separateLimitHint") : t("receiptCapture.limitHint")}</Footnote>
        </>
      )}

      {!started && onModeChange && files.length > 1 && (
        <section aria-labelledby="capture-mode-title">
          <SectionHeader id="capture-mode-title" action={<HelpLink topic="capture-batch" />}>{t("receiptCapture.modeLegend")}</SectionHeader>
          <IonList inset className="pm-after-header">
            <IonRadioGroup aria-labelledby="capture-mode-title" value={mode} onIonChange={(event) => onModeChange(event.detail.value as CaptureMode)}>
              <ModeOption value="parts" title={t("receiptCapture.modeParts")} hint={t("receiptCapture.modePartsHint")} disabled={uploading} />
              <ModeOption value="separate" title={t("receiptCapture.modeSeparate")} hint={t("receiptCapture.modeSeparateHint")} disabled={uploading} last />
            </IonRadioGroup>
          </IonList>
        </section>
      )}
      {separate && !started && <Footnote>{t("receiptCapture.separateNote", { count: files.length, max: receiptBatchLimits.maxPhotos })}</Footnote>}

      {files.length > 0 && (
        <section aria-labelledby="capture-files-title">
          <SectionHeader id="capture-files-title">{separate ? t("receiptCapture.separateListLabel") : t("receiptCapture.partsLabel")}</SectionHeader>
          <IonList inset className="pm-after-header" role="list" aria-label={separate ? t("receiptCapture.separateListLabel") : t("receiptCapture.partsLabel")}>
            {files.map((file, index) => {
              const state = batch?.[index];
              const content = (
                <>
                  <img slot="start" className="pm-thumb" src={previews[index]} alt={separate ? t("receiptCapture.previewReceiptAlt", { number: index + 1 }) : t("receiptCapture.previewAlt", { number: index + 1 })} />
                  <IonLabel className="pm-wrap">
                    <h3>{name(index)}</h3>
                    <p>{file.name}</p>
                    <p>{formatBytes(file.size, locale)}</p>
                    {state && <BatchPhotoStatus state={state} number={index + 1} running={uploading} onRetry={onRetryPhoto} />}
                  </IonLabel>
                </>
              );
              return started
                ? <IonItem key={`${file.name}-${file.lastModified}-${index}`} role="listitem">{content}</IonItem>
                : (
                  <IonItem key={`${file.name}-${file.lastModified}-${index}`} role="listitem" button detail={false} disabled={uploading} onClick={() => setActionsFor(index)}>
                    {content}
                    <IonNote slot="end">{t("ui.more")}</IonNote>
                  </IonItem>
                );
            })}
          </IonList>
        </section>
      )}

      <IonActionSheet
        isOpen={actionsFor !== null}
        header={actionsFor === null ? undefined : `${name(actionsFor)} · ${files[actionsFor]?.name ?? ""}`}
        onDidDismiss={() => setActionsFor(null)}
        buttons={actionsFor === null ? [] : [
          ...(!separate && actionsFor > 0 ? [{ text: t("receiptCapture.moveUp", { number: actionsFor + 1 }), handler: () => move(actionsFor, -1) }] : []),
          ...(!separate && actionsFor < files.length - 1 ? [{ text: t("receiptCapture.moveDown", { number: actionsFor + 1 }), handler: () => move(actionsFor, 1) }] : []),
          { text: separate ? t("receiptCapture.removeReceipt", { number: actionsFor + 1 }) : t("receiptCapture.remove", { number: actionsFor + 1 }), role: "destructive", handler: () => remove(actionsFor) },
          { text: t("ui.cancel"), role: "cancel" }
        ]}
      />

      {(validation ?? error) && <Footnote tone="danger" role="alert">{validation ?? error}</Footnote>}
      {started
        ? <BatchSummary batch={batch} running={uploading} disabled={disabled} onContinue={onSubmit} onStartOver={onStartOver} />
        : (
          <PrimaryButton disabled={files.length === 0 || uploading || disabled || validation !== null} loading={uploading} onClick={onSubmit}>
            {uploading ? t("receiptCapture.uploading") : separate ? t("receiptCapture.submitSeparate", { count: files.length }) : t("receiptCapture.submit")}
          </PrimaryButton>
        )}
    </>
  );
}

function ModeOption({ value, title, hint, disabled, last = false }: { value: CaptureMode; title: string; hint: string; disabled: boolean; last?: boolean }) {
  return (
    <IonItem lines={last ? "none" : undefined}>
      <IonRadio value={value} disabled={disabled} justify="space-between" labelPlacement="start">
        <span className="pm-radio-text"><strong>{title}</strong><small>{hint}</small></span>
      </IonRadio>
    </IonItem>
  );
}

function BatchPhotoStatus({ state, number, running, onRetry }: { state: BatchPhotoState; number: number; running: boolean; onRetry?: (index: number) => void }) {
  const { t } = useTranslation();
  const label = {
    waiting: running ? t("receiptCapture.statusWaiting") : t("receiptCapture.statusNotSent"),
    uploading: t("receiptCapture.statusUploading"),
    uploaded: t("receiptCapture.statusUploaded"),
    duplicate: t("receiptCapture.statusDuplicate"),
    failed: t("receiptCapture.statusFailed"),
    rateLimited: t("receiptCapture.statusRateLimited")
  }[state.status];
  const tone = state.status === "failed" || state.status === "rateLimited" ? "pm-bad"
    : state.status === "uploaded" || state.status === "duplicate" ? "pm-ok" : "";
  const canRetry = onRetry && (state.status === "failed" || state.status === "rateLimited");
  return (
    <div className="pm-batch-status">
      <span className={["pm-status", tone].filter(Boolean).join(" ")}>{label}</span>
      {state.status === "failed" && state.message && <small>{state.message}</small>}
      {(state.status === "uploaded" || state.status === "duplicate") && (
        <ButtonLink to={`/receipts/${state.receiptId}`} size="small" fill="clear" className="pm-inline-button">{t("receiptCapture.openReceipt")}</ButtonLink>
      )}
      {canRetry && (
        <IonButton size="small" fill="clear" className="pm-inline-button" disabled={running} aria-label={t("receiptCapture.retryPhoto", { number })} onClick={() => onRetry(number - 1)}>
          <RotateCcw aria-hidden="true" size={15} />&nbsp;{t("receiptCapture.retryShort")}
        </IonButton>
      )}
    </div>
  );
}

function BatchSummary({ batch, running, disabled, onContinue, onStartOver }: { batch: BatchPhotoState[]; running: boolean; disabled: boolean; onContinue: () => void; onStartOver?: () => void }) {
  const { t } = useTranslation();
  const total = batch.length;
  const done = batch.filter((photo) => photo.status === "uploaded" || photo.status === "duplicate").length;
  const current = batch.findIndex((photo) => photo.status === "uploading") + 1;
  const limited = batch.find((photo) => photo.status === "rateLimited");
  const remaining = total - done;
  return (
    <>
      <Footnote role="status">
        {running ? t("receiptCapture.batchProgress", { current: Math.max(current, done + 1), total }) : t("receiptCapture.batchDone", { done, total })}
      </Footnote>
      {!running && limited?.status === "rateLimited" && (
        <Footnote tone="danger" role="alert">
          {limited.retryAfterSeconds
            ? t("receiptCapture.batchRateLimited", { minutes: Math.max(1, Math.ceil(limited.retryAfterSeconds / 60)) })
            : t("receiptCapture.batchRateLimitedLater")}
        </Footnote>
      )}
      {!running && (
        <>
          {remaining > 0 && <PrimaryButton disabled={disabled} onClick={onContinue}>{t("receiptCapture.continueBatch")}</PrimaryButton>}
          <IonList inset>
            <ItemLink to="/"><IonLabel color="primary">{t("receiptCapture.goHome")}</IonLabel></ItemLink>
            {onStartOver && (
              <IonItem button detail={false} lines="none" onClick={onStartOver}>
                <IonLabel color="primary">{t("receiptCapture.newBatch")}</IonLabel>
              </IonItem>
            )}
          </IonList>
        </>
      )}
    </>
  );
}

function validateParts(files: File[], t: Translate): string | null {
  if (files.length > receiptCaptureLimits.maxParts) return t("receiptCapture.tooMany");
  if (files.some((file) => file.size > receiptCaptureLimits.maxPartBytes)) return t("receiptCapture.partTooLarge");
  if (files.reduce((sum, file) => sum + file.size, 0) > receiptCaptureLimits.maxTotalBytes) return t("receiptCapture.totalTooLarge");
  return validateTypes(files, t);
}

function validateSeparate(files: File[], t: Translate): string | null {
  if (files.length > receiptBatchLimits.maxPhotos) return t("receiptCapture.tooManySeparate", { max: receiptBatchLimits.maxPhotos });
  if (files.some((file) => file.size > receiptCaptureLimits.maxPartBytes)) return t("receiptCapture.photoTooLarge");
  return validateTypes(files, t);
}

function validateTypes(files: File[], t: Translate): string | null {
  if (files.some((file) => !["image/jpeg", "image/png", "image/webp"].includes(file.type))) return t("receiptCapture.unsupported");
  return null;
}

function formatBytes(bytes: number, locale: "ru" | "en") {
  return new Intl.NumberFormat(locale, { style: "unit", unit: "megabyte", maximumFractionDigits: 1 }).format(bytes / 1_000_000);
}
