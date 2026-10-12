import { useQuery } from "@tanstack/react-query";
import { ReceiptText } from "lucide-react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { listMyUnsavedReceipts } from "../../api/client";
import type { ReceiptView } from "../../api/types";
import { StatusBadge, type PurchaseStatus } from "../../design-system/components";
import { HelpLink } from "../help/HelpLink";
import type { RestoredBatch } from "./pendingBatchStore";
import styles from "./BatchRecovery.module.css";

export const myUnsavedReceiptsQueryKey = ["receipts", "my-unsaved"] as const;

const isActive = (receipt: ReceiptView) => receipt.status === "queued" || receipt.status === "processing";

/**
 * Fast while something is being recognised. After a failed fetch it polls
 * slowly instead of hammering a struggling server or a dead connection; the
 * next upload invalidates the list anyway.
 */
export function recentUploadsPollInterval(receipts: ReceiptView[] | undefined, error: unknown): number | false {
  if (!receipts?.some(isActive)) return false;
  return error ? 30_000 : 2_000;
}

/**
 * «Недавние загрузки»: the caller's unsaved receipts of the last day, straight
 * from the server. After a reload this is what tells the user which photos of
 * an interrupted batch already became receipts and how far each one got.
 */
export function RecentUploads() {
  const query = useQuery({
    queryKey: myUnsavedReceiptsQueryKey,
    queryFn: listMyUnsavedReceipts,
    refetchInterval: (current) => recentUploadsPollInterval(current.state.data, current.state.error)
  });
  // Auxiliary: the capture screen works without it, so a failed load stays quiet.
  return <RecentUploadsList receipts={query.data ?? []} />;
}

export function RecentUploadsList({ receipts }: { receipts: ReceiptView[] }) {
  const { t, i18n } = useTranslation();
  if (receipts.length === 0) return null;
  const time = new Intl.DateTimeFormat(i18n.resolvedLanguage === "en" ? "en" : "ru", { hour: "2-digit", minute: "2-digit" });
  return <section className={styles.recent} aria-labelledby="recent-uploads-heading">
    <div className={styles.heading}>
      <h2 id="recent-uploads-heading">{t("receiptCapture.recentTitle")}</h2>
      <p>{t("receiptCapture.recentHint")}</p>
      <HelpLink topic="capture-batch" />
    </div>
    <ul className={styles.list}>
      {receipts.map((receipt) => <li key={receipt.id}>
        <Link className={styles.row} to={`/receipts/${receipt.id}`}>
          <ReceiptText aria-hidden="true" size={20} />
          <span className={styles.copy}>
            <strong>{receipt.snapshot?.merchantName ?? t("receiptJobs.receipt")}</strong>
            <small>{time.format(new Date(receipt.uploadedAt))}</small>
          </span>
          <StatusBadge status={badgeStatus(receipt)} />
        </Link>
      </li>)}
    </ul>
  </section>;
}

function badgeStatus(receipt: ReceiptView): PurchaseStatus {
  switch (receipt.status) {
    case "queued":
    case "processing":
      return "processing";
    case "ready":
      // Same rule as Home: any warning means the receipt needs a look.
      return receipt.warnings.length > 0 ? "needsAttention" : "ready";
    case "failed":
      return "failed";
    default:
      return "uploaded";
  }
}

/**
 * Shown after a reload (or a return to the screen) when the device still
 * holds photos of a batch that may not have reached the server.
 */
export function InterruptedBatchNotice({ restored, disabled, onResume, onDiscard }: { restored: RestoredBatch; disabled: boolean; onResume: () => void; onDiscard: () => void }) {
  const { t } = useTranslation();
  const kept = restored.files.length;
  const lost = restored.lost.length;
  return <section className={styles.interrupted} aria-labelledby="interrupted-batch-heading">
    <h2 id="interrupted-batch-heading">{t("receiptCapture.interruptedTitle")}</h2>
    {kept > 0 && <p>{t("receiptCapture.interruptedKept", { count: kept })}</p>}
    {lost > 0 && <>
      <p>{t("receiptCapture.interruptedLost", { count: lost })}</p>
      <ul className={styles.lostList} aria-label={t("receiptCapture.interruptedLostLabel")}>
        {restored.lost.map((photo, index) => <li key={`${photo.name}-${index}`}>{photo.name}</li>)}
      </ul>
    </>}
    <p className={styles.muted}>{t("receiptCapture.interruptedSafe")}</p>
    <p className={styles.muted}>{kept > 0 ? t("receiptCapture.interruptedFirst") : t("receiptCapture.interruptedFirstLost")}</p>
    <HelpLink topic="capture-batch" />
    <div className={styles.actions}>
      {kept > 0 && <button className={styles.primary} disabled={disabled} onClick={onResume}>{t("receiptCapture.resumeBatch", { count: kept })}</button>}
      <button className={styles.secondary} onClick={onDiscard}>{kept > 0 ? t("receiptCapture.discardBatch") : t("receiptCapture.dismissInterrupted")}</button>
    </div>
  </section>;
}
