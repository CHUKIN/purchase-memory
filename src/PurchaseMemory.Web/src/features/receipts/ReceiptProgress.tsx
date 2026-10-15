import { IonItem, IonLabel, IonList, IonProgressBar } from "@ionic/react";
import { useTranslation } from "react-i18next";
import type { ReceiptView } from "../../api/types";
import { useAuthorizedImageUrl } from "../../auth/useAuthorizedImageUrl";
import { Footnote, PrimaryButton } from "../../design-system/components";

/** Where the receipt is in its pipeline: status, progress, the stored photos and a retry. */
export function ReceiptProgress({ receipt, retrying = false, onRetry }: { receipt: ReceiptView; retrying?: boolean; onRetry?: () => void }) {
  const { t } = useTranslation();
  const ready = receipt.status === "ready";
  const needsAttention = ready && receipt.warnings.length > 0;
  const label = needsAttention ? t("status.needsAttention") : t(`status.${receipt.status === "queued" ? "processing" : receipt.status}`);
  const value = receipt.status === "uploaded" ? 20 : receipt.status === "queued" ? 40 : receipt.status === "processing" ? 70 : 100;
  // A saved receipt says so in its title and row; the others get one hint.
  const help = receipt.status === "failed"
    ? t("receiptProgress.failedHelp")
    : receipt.status === "saved" || ready
      ? null
      : t("receiptProgress.safeToLeave");
  return (
    <section aria-live="polite" aria-label={t(`receiptProgress.${receipt.status}`)}>
      {/* A recognized draft is described by the page title; only its photos stay. */}
      {!ready && <IonList inset>
        <IonItem lines="none">
          <IonLabel className="pm-wrap">
            <h2>{t(`receiptProgress.${receipt.status}`)}</h2>
            {/* One photo is the usual case; the thumbnails below show the parts anyway. */}
            {receipt.imageParts.length > 1 && <p>{t("receiptProgress.parts", { count: receipt.imageParts.length })}</p>}
            <IonProgressBar className="pm-job-progress" value={value / 100} color={receipt.status === "failed" ? "danger" : "primary"} aria-label={label} />
          </IonLabel>
        </IonItem>
      </IonList>}
      {help && <Footnote>{help}</Footnote>}
      <div className="pm-thumbs">
        {receipt.imageParts.map((part) => <PartImage key={part.position} url={part.sourceImageUrl} alt={t("receiptProgress.partAlt", { number: part.position + 1 })} />)}
      </div>
      {receipt.error && <Footnote tone="danger" role="alert">{receipt.error}</Footnote>}
      {(receipt.status === "failed" || receipt.status === "uploaded") && onRetry && (
        <PrimaryButton loading={retrying} onClick={onRetry}>
          {retrying ? t("receiptProgress.retrying") : receipt.status === "uploaded" ? t("receiptProgress.startProcessing") : t("receiptProgress.retry")}
        </PrimaryButton>
      )}
    </section>
  );
}

/** Receipt photos are protected, so each part is fetched with the session token. */
function PartImage({ url, alt }: { url: string; alt: string }) {
  const { src } = useAuthorizedImageUrl(url);
  return src ? <img src={src} alt={alt} /> : <span role="img" aria-label={alt} />;
}
