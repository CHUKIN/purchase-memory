import { ArrowDown, ArrowUp, Camera, Images, Trash2 } from "lucide-react";
import { useEffect, useId, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import styles from "./ReceiptCapture.module.css";

export const receiptCaptureLimits = { maxParts: 8, maxPartBytes: 15 * 1024 * 1024, maxTotalBytes: 60 * 1024 * 1024 } as const;

export interface ReceiptCaptureProps {
  files: File[];
  uploading?: boolean;
  disabled?: boolean;
  error?: string | null;
  onFilesChange: (files: File[]) => void;
  onSubmit: () => void;
}

export function ReceiptCapture({ files, uploading = false, disabled = false, error, onFilesChange, onSubmit }: ReceiptCaptureProps) {
  const { t, i18n } = useTranslation();
  const cameraId = useId();
  const galleryId = useId();
  const [previews, setPreviews] = useState<string[]>([]);

  useEffect(() => {
    const urls = files.map((file) => URL.createObjectURL(file));
    setPreviews(urls);
    return () => urls.forEach((url) => URL.revokeObjectURL(url));
  }, [files]);

  const validation = useMemo(() => validateFiles(files, t), [files, t]);
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

  return <div className={styles.root}>
    <div className={styles.actions}>
      <input id={cameraId} className="visually-hidden" type="file" accept="image/jpeg,image/png,image/webp" capture="environment" onChange={(event) => { append(event.target.files); event.target.value = ""; }} />
      <label className={`${styles.fileAction} ${styles.cameraAction}`} htmlFor={cameraId}><Camera aria-hidden="true" size={20} />{t("receiptCapture.camera")}</label>
      <input id={galleryId} className="visually-hidden" type="file" accept="image/jpeg,image/png,image/webp" multiple onChange={(event) => { append(event.target.files); event.target.value = ""; }} />
      <label className={styles.fileAction} htmlFor={galleryId}><Images aria-hidden="true" size={20} />{t("receiptCapture.gallery")}</label>
    </div>
    <p className={styles.hint}>{t("receiptCapture.limitHint")}</p>
    {files.length > 0 && <ol className={styles.parts} aria-label={t("receiptCapture.partsLabel")}>
      {files.map((file, index) => <li className={styles.part} key={`${file.name}-${file.lastModified}-${index}`}>
        <img src={previews[index]} alt={t("receiptCapture.previewAlt", { number: index + 1 })} />
        <div className={styles.partCopy}><strong>{t("receiptCapture.part", { number: index + 1 })}</strong><span>{file.name}</span><span>{formatBytes(file.size, i18n.resolvedLanguage === "en" ? "en" : "ru")}</span></div>
        <div className={styles.partActions}>
          <button className={styles.iconButton} disabled={index === 0} aria-label={t("receiptCapture.moveUp", { number: index + 1 })} onClick={() => move(index, -1)}><ArrowUp aria-hidden="true" size={18} /></button>
          <button className={styles.iconButton} disabled={index === files.length - 1} aria-label={t("receiptCapture.moveDown", { number: index + 1 })} onClick={() => move(index, 1)}><ArrowDown aria-hidden="true" size={18} /></button>
          <button className={`${styles.iconButton} ${styles.remove}`} aria-label={t("receiptCapture.remove", { number: index + 1 })} onClick={() => onFilesChange(files.filter((_, position) => position !== index))}><Trash2 aria-hidden="true" size={18} /></button>
        </div>
      </li>)}
    </ol>}
    {(validation ?? error) && <p className={styles.error} role="alert">{validation ?? error}</p>}
    <button className={styles.submit} disabled={files.length === 0 || uploading || disabled || validation !== null} onClick={onSubmit}>{uploading ? t("receiptCapture.uploading") : t("receiptCapture.submit")}</button>
  </div>;
}

function validateFiles(files: File[], t: (key: string, options?: Record<string, unknown>) => string): string | null {
  if (files.length > receiptCaptureLimits.maxParts) return t("receiptCapture.tooMany");
  if (files.some((file) => file.size > receiptCaptureLimits.maxPartBytes)) return t("receiptCapture.partTooLarge");
  if (files.reduce((sum, file) => sum + file.size, 0) > receiptCaptureLimits.maxTotalBytes) return t("receiptCapture.totalTooLarge");
  if (files.some((file) => !["image/jpeg", "image/png", "image/webp"].includes(file.type))) return t("receiptCapture.unsupported");
  return null;
}

function formatBytes(bytes: number, locale: "ru" | "en") {
  return new Intl.NumberFormat(locale, { style: "unit", unit: "megabyte", maximumFractionDigits: 1 }).format(bytes / 1_000_000);
}
