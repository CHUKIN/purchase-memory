import { useTranslation } from "react-i18next";
import type { ReceiptView } from "../../api/types";
import { useAuthorizedImageUrl } from "../../auth/useAuthorizedImageUrl";
import styles from "./ReceiptProgress.module.css";

export function ReceiptProgress({ receipt, retrying = false, onRetry }: { receipt: ReceiptView; retrying?: boolean; onRetry?: () => void }) {
  const { t } = useTranslation();
  const needsAttention = receipt.status === "ready" && receipt.warnings.length > 0;
  const label = needsAttention ? t("status.needsAttention") : t(`status.${receipt.status === "queued" ? "processing" : receipt.status}`);
  const value = receipt.status === "uploaded" ? 20 : receipt.status === "queued" ? 40 : receipt.status === "processing" ? 70 : 100;
  const help = receipt.status === "ready"
    ? t("receiptProgress.openReview")
    : receipt.status === "failed"
      ? t("receiptProgress.failedHelp")
      : receipt.status === "saved"
        ? t("receiptProgress.savedHelp")
        : t("receiptProgress.safeToLeave");
  return <section className={styles.root} aria-live="polite">
    <div className={styles.heading}><div><p>{t("receiptProgress.parts", { count: receipt.imageParts.length })}</p><h2>{t(`receiptProgress.${receipt.status}`)}</h2><p>{help}</p></div><span className={`${styles.badge} ${receipt.status === "failed" ? styles.danger : needsAttention ? styles.warning : ""}`}>{label}</span></div>
    <progress className={styles.progress} aria-label={label} max="100" value={value}>{value}%</progress>
    <div className={styles.parts}>{receipt.imageParts.map((part) => <PartImage key={part.position} url={part.sourceImageUrl} alt={t("receiptProgress.partAlt", { number: part.position + 1 })} />)}</div>
    {receipt.error && <p className={styles.error} role="alert">{receipt.error}</p>}
    {(receipt.status === "failed" || receipt.status === "uploaded") && onRetry && <button className={styles.action} disabled={retrying} onClick={onRetry}>{retrying ? t("receiptProgress.retrying") : receipt.status === "uploaded" ? t("receiptProgress.startProcessing") : t("receiptProgress.retry")}</button>}
  </section>;
}

/** Receipt photos are protected, so each part is fetched with the session token. */
function PartImage({ url, alt }: { url: string; alt: string }) {
  const { src } = useAuthorizedImageUrl(url);
  return src ? <img src={src} alt={alt} /> : <span role="img" aria-label={alt} />;
}
