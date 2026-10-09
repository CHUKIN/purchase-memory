import { Trash2 } from "lucide-react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import type { Impression } from "../../api/types";
import { useAuthorizedImageUrl } from "../../auth/useAuthorizedImageUrl";
import { Button, IconButton } from "../../design-system/components";
import { formatMoney } from "../purchases/money";
import { compareByStore, groupByAuthor, ratingLabel, ratings } from "./impressionText";
import { RatingIcon } from "./RatingControl";
import styles from "./Impressions.module.css";

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
    return <p className={styles.muted}>{t("impressions.empty")}</p>;
  }

  return (
    <div className={styles.people}>
      {groupByAuthor(t, impressions).map((group) => (
        <section key={group.authorUserId} className={styles.person} aria-label={group.label}>
          <Heading>{group.label} · {t("impressions.count", { count: group.impressions.length })}</Heading>
          <ul className={styles.history}>
            {group.impressions.map((impression) => (
              <ImpressionEntry key={impression.id} impression={impression} locale={locale} onDelete={impression.isMine ? onDelete : undefined} />
            ))}
          </ul>
        </section>
      ))}
    </div>
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
    <li className={styles.entry}>
      <div className={styles.entryHeader}>
        <span className={styles.badge} data-rating={impression.rating}>
          <RatingIcon rating={impression.rating} />
          {ratingLabel(t, impression.rating)}
        </span>
        <time className={styles.muted} dateTime={impression.createdAt}>
          {t("impressions.ratedOn", { date: formatDay(locale, impression.createdAt) })}
        </time>
      </div>
      <p className={styles.muted}>{context}</p>
      {impression.note && <p className={styles.note}>{impression.note}</p>}
      {photo.src && <img className={styles.photo} src={photo.src} alt={t("impressions.photoAlt", { name: impression.itemName })} />}
      {photo.failed && <p className={styles.muted}>{t("impressions.photoFailed")}</p>}
      {onDelete && (
        <div className={`${styles.formActions} ${styles.entryActions}`}>
          {confirming
            ? (
              <>
                <span className={styles.muted}>{t("impressions.deleteConfirm")}</span>
                <Button intent="danger" loading={busy} onClick={() => void remove()}>{t("impressions.deleteYes")}</Button>
                <Button intent="quiet" disabled={busy} onClick={() => setConfirming(false)}>{t("impressions.cancel")}</Button>
              </>
            )
            : <IconButton intent="danger" iconOnly="mobile" icon={<Trash2 />} label={t("impressions.delete")} onClick={() => setConfirming(true)} />}
        </div>
      )}
      {failed && <p className={styles.error} role="alert">{t("impressions.deleteFailed")}</p>}
    </li>
  );
}

/** Per store: how many of each rating, and each member's latest word there. */
export function StoreComparison({ impressions }: { impressions: Impression[] }) {
  const { t } = useTranslation();
  const stores = compareByStore(t, impressions);
  if (stores.length < 2) {
    return <p className={styles.muted}>{t("impressions.comparisonNeedsTwoStores")}</p>;
  }

  return (
    <div className={styles.tableScroll}>
      <table className={styles.comparison}>
        <caption className="visually-hidden">{t("impressions.comparisonCaption")}</caption>
        <thead>
          <tr>
            <th scope="col">{t("impressions.store")}</th>
            {ratings.map((rating) => <th key={rating} scope="col">{ratingLabel(t, rating)}</th>)}
            <th scope="col">{t("impressions.latestByPerson")}</th>
          </tr>
        </thead>
        <tbody>
          {stores.map((store) => (
            <tr key={store.store ?? ""}>
              <th scope="row">{store.store ?? t("impressions.unknownStore")}</th>
              {ratings.map((rating) => <td key={rating} className={styles.count}>{store.counts[rating]}</td>)}
              <td>{store.latestByAuthor.map((entry) => `${entry.label}: ${ratingLabel(t, entry.rating)}`).join("; ")}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
