import { IonItem, IonLabel, IonList } from "@ionic/react";
import { useQuery } from "@tanstack/react-query";
import { ChevronDown } from "lucide-react";
import { useId, useState } from "react";
import { useTranslation } from "react-i18next";
import { getReceiptAuditTrail } from "../../api/client";
import type { ReceiptReviewAuditEntry } from "../../api/types";

/**
 * A collapsible audit trail of the corrections made during review, fetched
 * lazily so a receipt with no history never pays for the request. It refetches
 * whenever the audit-trail query is invalidated (the same invalidation the
 * review submit already triggers), so a fix just applied shows up here too.
 */
export function ReviewHistory({ receiptId }: { receiptId: string }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const historyId = useId();
  const query = useQuery({
    queryKey: ["receipts", receiptId, "auditTrail"],
    queryFn: () => getReceiptAuditTrail(receiptId),
    enabled: open
  });

  return (
    <section>
      <IonList inset>
        <button type="button" className="pm-disclosure" data-open={open} aria-expanded={open} aria-controls={historyId} onClick={() => setOpen((current) => !current)}>
          <span className="pm-disclosure-label">{open ? t("review.history.toggleHide") : t("review.history.toggleShow")}</span>
          <ChevronDown size={20} aria-hidden="true" className="pm-chevron" data-open={open} />
        </button>
        {open && (
          <div id={historyId}>
            <h3 className="visually-hidden">{t("review.history.title")}</h3>
            {query.isLoading && <IonItem lines="none"><IonLabel color="medium">{t("review.history.loading")}</IonLabel></IonItem>}
            {query.isError && <IonItem lines="none"><IonLabel color="medium">{t("review.history.loadFailed")}</IonLabel></IonItem>}
            {query.data && query.data.length === 0 && <IonItem lines="none"><IonLabel color="medium">{t("review.history.empty")}</IonLabel></IonItem>}
            {query.data?.map((entry, index) => (
              <IonItem key={`${entry.occurredAt}-${entry.fieldName}-${index}`} lines={index === query.data.length - 1 ? "none" : undefined}>
                <HistoryEntryLine entry={entry} />
              </IonItem>
            ))}
          </div>
        )}
      </IonList>
    </section>
  );
}

function HistoryEntryLine({ entry }: { entry: ReceiptReviewAuditEntry }) {
  const { t } = useTranslation();
  const summaryKey =
    entry.changeType === "ItemAdded"
      ? "review.history.itemAdded"
      : entry.changeType === "ItemRemoved"
        ? "review.history.itemRemoved"
        : "review.history.modified";

  return (
    <IonLabel className="pm-wrap">
      <h3>{t(summaryKey, { field: entry.fieldName })}</h3>
      <p>
        {entry.oldValue !== null && `${t("review.history.from", { value: entry.oldValue })} `}
        {entry.newValue !== null && t("review.history.to", { value: entry.newValue })}
      </p>
      <p>{t("review.history.actor", { actor: entry.actor })} · {new Date(entry.occurredAt).toLocaleString()}</p>
    </IonLabel>
  );
}
