import { ArrowDown, ArrowUp, Camera, Images, RotateCcw, Trash2 } from "lucide-react";
import { useEffect, useId, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { IconButton } from "../../design-system/components";
import { HelpLink } from "../help/HelpLink";
import type { BatchPhotoState } from "./useReceiptBatchUpload";
import styles from "./ReceiptCapture.module.css";

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

export function ReceiptCapture({ files, uploading = false, disabled = false, error, mode = "parts", onModeChange, batch = null, onRetryPhoto, onStartOver, onFilesChange, onSubmit }: ReceiptCaptureProps) {
  const { t, i18n } = useTranslation();
  const cameraId = useId();
  const galleryId = useId();
  const modeName = useId();
  const [previews, setPreviews] = useState<string[]>([]);
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

  return <div className={styles.root}>
    {!started && <>
      <div className={styles.actions}>
        <input id={cameraId} className="visually-hidden" type="file" accept="image/jpeg,image/png,image/webp" capture="environment" onChange={(event) => { append(event.target.files); event.target.value = ""; }} />
        <label className={`${styles.fileAction} ${styles.cameraAction}`} htmlFor={cameraId}><Camera aria-hidden="true" size={20} />{t("receiptCapture.camera")}</label>
        <input id={galleryId} className="visually-hidden" type="file" accept="image/jpeg,image/png,image/webp" multiple onChange={(event) => { append(event.target.files); event.target.value = ""; }} />
        <label className={styles.fileAction} htmlFor={galleryId}><Images aria-hidden="true" size={20} />{t("receiptCapture.gallery")}</label>
      </div>
      <p className={styles.hint}>{separate ? t("receiptCapture.separateLimitHint") : t("receiptCapture.limitHint")}</p>
    </>}
    {!started && onModeChange && files.length > 1 && <fieldset className={styles.modes} disabled={uploading}>
      <legend>{t("receiptCapture.modeLegend")}</legend>
      <ModeOption name={modeName} value="parts" checked={mode === "parts"} title={t("receiptCapture.modeParts")} hint={t("receiptCapture.modePartsHint")} onChange={onModeChange} />
      <ModeOption name={modeName} value="separate" checked={mode === "separate"} title={t("receiptCapture.modeSeparate")} hint={t("receiptCapture.modeSeparateHint")} onChange={onModeChange} />
      <HelpLink topic="capture-batch" />
    </fieldset>}
    {separate && !started && <p className={styles.note}>{t("receiptCapture.separateNote", { count: files.length, max: receiptBatchLimits.maxPhotos })}</p>}
    {files.length > 0 && <ol className={styles.parts} aria-label={separate ? t("receiptCapture.separateListLabel") : t("receiptCapture.partsLabel")}>
      {files.map((file, index) => <li className={styles.part} key={`${file.name}-${file.lastModified}-${index}`}>
        <img src={previews[index]} alt={separate ? t("receiptCapture.previewReceiptAlt", { number: index + 1 }) : t("receiptCapture.previewAlt", { number: index + 1 })} />
        <div className={styles.partCopy}>
          <strong>{separate ? t("receiptCapture.receipt", { number: index + 1 }) : t("receiptCapture.part", { number: index + 1 })}</strong>
          <span>{file.name}</span>
          <span>{formatBytes(file.size, locale)}</span>
        </div>
        {batch?.[index]
          ? <BatchPhotoStatus state={batch[index]} number={index + 1} running={uploading} onRetry={onRetryPhoto} />
          : !started && <div className={styles.partActions}>
            {!separate && <>
              <IconButton className={styles.iconButton} disabled={index === 0 || uploading} label={t("receiptCapture.moveUp", { number: index + 1 })} icon={<ArrowUp size={18} />} onClick={() => move(index, -1)} />
              <IconButton className={styles.iconButton} disabled={index === files.length - 1 || uploading} label={t("receiptCapture.moveDown", { number: index + 1 })} icon={<ArrowDown size={18} />} onClick={() => move(index, 1)} />
            </>}
            <IconButton className={styles.iconButton} intent="danger" disabled={uploading} label={separate ? t("receiptCapture.removeReceipt", { number: index + 1 }) : t("receiptCapture.remove", { number: index + 1 })} icon={<Trash2 size={18} />} onClick={() => remove(index)} />
          </div>}
      </li>)}
    </ol>}
    {(validation ?? error) && <p className={styles.error} role="alert">{validation ?? error}</p>}
    {started
      ? <BatchSummary batch={batch} running={uploading} disabled={disabled} onContinue={onSubmit} onStartOver={onStartOver} />
      : <button className={styles.submit} disabled={files.length === 0 || uploading || disabled || validation !== null} onClick={onSubmit}>
        {uploading ? t("receiptCapture.uploading") : separate ? t("receiptCapture.submitSeparate", { count: files.length }) : t("receiptCapture.submit")}
      </button>}
  </div>;
}

function ModeOption({ name, value, checked, title, hint, onChange }: { name: string; value: CaptureMode; checked: boolean; title: string; hint: string; onChange: (mode: CaptureMode) => void }) {
  const hintId = useId();
  return <label className={styles.mode}>
    <input type="radio" name={name} value={value} checked={checked} aria-describedby={hintId} onChange={() => onChange(value)} />
    <span><strong>{title}</strong><small id={hintId}>{hint}</small></span>
  </label>;
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
  const tone = state.status === "failed" || state.status === "rateLimited" ? styles.statusDanger
    : state.status === "uploaded" || state.status === "duplicate" ? styles.statusDone : "";
  const canRetry = onRetry && (state.status === "failed" || state.status === "rateLimited");
  return <div className={styles.batchStatus}>
    <span className={`${styles.statusBadge} ${tone}`}>{label}</span>
    {state.status === "failed" && state.message && <small className={styles.statusDetail}>{state.message}</small>}
    {(state.status === "uploaded" || state.status === "duplicate") && <Link className={styles.statusLink} to={`/receipts/${state.receiptId}`}>{t("receiptCapture.openReceipt")}</Link>}
    {canRetry && <button className={styles.retry} disabled={running} aria-label={t("receiptCapture.retryPhoto", { number })} onClick={() => onRetry(number - 1)}><RotateCcw aria-hidden="true" size={16} />{t("receiptCapture.retryShort")}</button>}
  </div>;
}

function BatchSummary({ batch, running, disabled, onContinue, onStartOver }: { batch: BatchPhotoState[]; running: boolean; disabled: boolean; onContinue: () => void; onStartOver?: () => void }) {
  const { t } = useTranslation();
  const total = batch.length;
  const done = batch.filter((photo) => photo.status === "uploaded" || photo.status === "duplicate").length;
  const current = batch.findIndex((photo) => photo.status === "uploading") + 1;
  const limited = batch.find((photo) => photo.status === "rateLimited");
  const remaining = total - done;
  return <div className={styles.summary}>
    <p role="status" className={styles.summaryText}>
      {running ? t("receiptCapture.batchProgress", { current: Math.max(current, done + 1), total }) : t("receiptCapture.batchDone", { done, total })}
    </p>
    {!running && limited?.status === "rateLimited" && <p className={styles.error} role="alert">
      {limited.retryAfterSeconds
        ? t("receiptCapture.batchRateLimited", { minutes: Math.max(1, Math.ceil(limited.retryAfterSeconds / 60)) })
        : t("receiptCapture.batchRateLimitedLater")}
    </p>}
    {!running && <div className={styles.summaryActions}>
      {remaining > 0 && <button className={styles.submit} disabled={disabled} onClick={onContinue}>{t("receiptCapture.continueBatch")}</button>}
      <Link className={styles.secondary} to="/">{t("receiptCapture.goHome")}</Link>
      {onStartOver && <button className={styles.secondary} onClick={onStartOver}>{t("receiptCapture.newBatch")}</button>}
    </div>}
  </div>;
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
