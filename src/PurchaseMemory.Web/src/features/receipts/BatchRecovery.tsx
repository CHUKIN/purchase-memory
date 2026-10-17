import { IonItem, IonLabel, IonList, IonNote } from "@ionic/react";
import { useQuery } from "@tanstack/react-query";
import { ReceiptText } from "lucide-react";
import { useTranslation } from "react-i18next";
import { useReturnState } from "../../app/backNavigation";
import { listMyUnsavedReceipts } from "../../api/client";
import type { ReceiptView } from "../../api/types";
import { ActionButton, Footnote, ItemLink, Notice, SectionHeader, StatusText, type PurchaseStatus } from "../../design-system/components";
import { HelpLink } from "../help/HelpLink";
import type { RestoredBatch } from "./pendingBatchStore";

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
  const returnState = useReturnState();
  if (receipts.length === 0) return null;
  const time = new Intl.DateTimeFormat(i18n.resolvedLanguage === "en" ? "en" : "ru", { hour: "2-digit", minute: "2-digit" });
  return (
    <section aria-labelledby="recent-uploads-heading">
      <SectionHeader id="recent-uploads-heading" action={<HelpLink topic="capture-batch" />}>{t("receiptCapture.recentTitle")}</SectionHeader>
      <IonList inset className="pm-after-header">
        {receipts.map((receipt) => (
          <ItemLink key={receipt.id} to={`/receipts/${receipt.id}`} state={returnState}>
            <ReceiptText slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
            <IonLabel className="pm-wrap">
              <h3>{receipt.snapshot?.merchantName ?? t("receiptJobs.receipt")}</h3>
              <p>{time.format(new Date(receipt.uploadedAt))}</p>
            </IonLabel>
            <IonNote slot="end"><StatusText status={badgeStatus(receipt)} /></IonNote>
          </ItemLink>
        ))}
      </IonList>
    </section>
  );
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
  return (
    <section aria-labelledby="interrupted-batch-heading">
      <SectionHeader id="interrupted-batch-heading" action={<HelpLink topic="capture-batch" />}>{t("receiptCapture.interruptedTitle")}</SectionHeader>
      <Notice tone="warning">
        {kept > 0 && <p>{t("receiptCapture.interruptedKept", { count: kept })}</p>}
        {lost > 0 && <p>{t("receiptCapture.interruptedLost", { count: lost })}</p>}
      </Notice>
      {lost > 0 && (
        <IonList inset aria-label={t("receiptCapture.interruptedLostLabel")}>
          {restored.lost.map((photo, index) => (
            <IonItem key={`${photo.name}-${index}`} lines={index === lost - 1 ? "none" : undefined}>
              <IonLabel className="pm-wrap">{photo.name}</IonLabel>
            </IonItem>
          ))}
        </IonList>
      )}
      <Footnote>{kept > 0 ? t("receiptCapture.interruptedFirst") : t("receiptCapture.interruptedFirstLost")}</Footnote>
      <div className="pm-primary-block">
        {kept > 0 && <ActionButton expand="block" shape="round" disabled={disabled} onClick={onResume}>{t("receiptCapture.resumeBatch", { count: kept })}</ActionButton>}
        <ActionButton expand="block" fill="clear" onClick={onDiscard}>{kept > 0 ? t("receiptCapture.discardBatch") : t("receiptCapture.dismissInterrupted")}</ActionButton>
      </div>
    </section>
  );
}
