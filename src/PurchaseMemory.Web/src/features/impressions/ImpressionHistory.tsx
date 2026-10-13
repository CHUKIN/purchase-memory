import { IonActionSheet, IonButton, IonItem, IonLabel, IonList } from "@ionic/react";
import { Trash2 } from "lucide-react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import type { Impression } from "../../api/types";
import { useAuthorizedImageUrl } from "../../auth/useAuthorizedImageUrl";
import { Footnote } from "../../design-system/components";
import { formatMoney } from "../purchases/money";
import { compareByStore, groupByAuthor, ratingLabel, ratings } from "./impressionText";
import { RatingIcon } from "./RatingControl";

function formatDay(locale: string, value: string): string {
  return new Intl.DateTimeFormat(locale, { dateStyle: "medium" }).format(new Date(value));
}

export interface ImpressionHistoryProps {
  impressions: Impression[];
  locale: string;
  /** Heading level of each person's group; the section around it owns the level above. */
  headingLevel?: 3 | 4;
  /** Withdraws one of the viewer's own impressions. */
  onDelete?: (impressionId: string) => Promise<unknown>;
}

/** Each household member's opinions, newest first, with the purchase they were about. */
export function ImpressionHistory({ impressions, locale, headingLevel = 3, onDelete }: ImpressionHistoryProps) {
  const { t } = useTranslation();
  const Heading = headingLevel === 3 ? "h3" : "h4";
  if (impressions.length === 0) {
    return <Footnote>{t("impressions.empty")}</Footnote>;
  }

  return (
    <>
      {groupByAuthor(t, impressions).map((group) => (
        <section key={group.authorUserId} aria-label={group.label}>
          <div className="pm-section-header"><Heading>{group.label} · {t("impressions.count", { count: group.impressions.length })}</Heading></div>
          <IonList inset className="pm-after-header">
            {group.impressions.map((impression) => (
              <ImpressionEntry key={impression.id} impression={impression} locale={locale} onDelete={impression.isMine ? onDelete : undefined} />
            ))}
          </IonList>
        </section>
      ))}
    </>
  );
}

function ImpressionEntry({ impression, locale, onDelete }: { impression: Impression; locale: string; onDelete?: (impressionId: string) => Promise<unknown> }) {
  const { t } = useTranslation();
  const photo = useAuthorizedImageUrl(impression.photoUrl);
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);
  const price = impression.unitPrice ?? impression.lineTotal;
  const context = [
    impression.merchantName ?? t("impressions.unknownStore"),
    impression.purchasedAt ? formatDay(locale, impression.purchasedAt) : null,
    price === null ? null : formatMoney(price, impression.currency, locale)
  ].filter(Boolean).join(" · ");

  const remove = async () => {
    if (!onDelete) return;
    setBusy(true);
    setFailed(false);
    try {
      await onDelete(impression.id);
    } catch {
      setFailed(true);
      setBusy(false);
    }
  };

  return (
    <IonItem>
      <IonLabel className="pm-wrap">
        <p className="pm-rating-line" data-rating={impression.rating}>
          <RatingIcon rating={impression.rating} />
          <strong>{ratingLabel(t, impression.rating)}</strong>
        </p>
        <p><time dateTime={impression.createdAt}>{t("impressions.ratedOn", { date: formatDay(locale, impression.createdAt) })}</time></p>
        <p>{context}</p>
        {impression.note && <p className="pm-note-text">{impression.note}</p>}
        {photo.src && <img className="pm-impression-photo" src={photo.src} alt={t("impressions.photoAlt", { name: impression.itemName })} />}
        {photo.failed && <p>{t("impressions.photoFailed")}</p>}
        {failed && <p className="pm-danger-text" role="alert">{t("impressions.deleteFailed")}</p>}
      </IonLabel>
      {onDelete && (
        <>
          <IonButton slot="end" fill="clear" color="danger" disabled={busy} aria-label={t("impressions.delete")} onClick={() => setConfirming(true)}>
            <Trash2 size={20} aria-hidden="true" />
          </IonButton>
          <IonActionSheet
            isOpen={confirming}
            header={t("impressions.deleteConfirm")}
            onDidDismiss={() => setConfirming(false)}
            buttons={[
              { text: t("impressions.deleteYes"), role: "destructive", handler: () => { void remove(); } },
              { text: t("impressions.cancel"), role: "cancel" }
            ]}
          />
        </>
      )}
    </IonItem>
  );
}

/** Per store: how many of each rating, and each member's latest word there. */
export function StoreComparison({ impressions }: { impressions: Impression[] }) {
  const { t } = useTranslation();
  const stores = compareByStore(t, impressions);
  if (stores.length < 2) {
    return <Footnote>{t("impressions.comparisonNeedsTwoStores")}</Footnote>;
  }

  return (
    <div className="pm-table-card">
      <table className="pm-table">
        <caption className="visually-hidden">{t("impressions.comparisonCaption")}</caption>
        <thead>
          <tr>
            <th scope="col">{t("impressions.store")}</th>
            {ratings.map((rating) => <th key={rating} scope="col" className="pm-number">{ratingLabel(t, rating)}</th>)}
            <th scope="col">{t("impressions.latestByPerson")}</th>
          </tr>
        </thead>
        <tbody>
          {stores.map((store) => (
            <tr key={store.store ?? ""}>
              <th scope="row">{store.store ?? t("impressions.unknownStore")}</th>
              {ratings.map((rating) => <td key={rating} className="pm-number">{store.counts[rating]}</td>)}
              <td>{store.latestByAuthor.map((entry) => `${entry.label}: ${ratingLabel(t, entry.rating)}`).join("; ")}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
