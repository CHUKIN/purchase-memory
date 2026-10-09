import { History, MessageSquareReply, RefreshCw, Sparkles, Trash2 } from "lucide-react";
import { useId, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import type { AnalyticsHistoryItem } from "../../api/types";
import { Button, IconButton, Surface } from "../../design-system/components";
import { HelpLink } from "../help/HelpLink";
import styles from "./AnalyticsHistory.module.css";

/** Where the answer on screen came from, when it is a saved one. */
export interface SavedAnswerInfo {
  entryId: string;
  question: string;
  /** When the shown numbers were computed. */
  answeredAt: string;
  /** `history`: opened from «Предыдущие вопросы»; `reused`: the same words were asked again and the saved plan ran. */
  origin: "history" | "reused";
  isFollowUp: boolean;
}

function formatMoment(value: string, locale: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : new Intl.DateTimeFormat(locale, { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" }).format(date);
}

export interface SavedAnswerBarProps {
  saved: SavedAnswerInfo;
  locale: string;
  /** False when questions are off on the server: «Спросить заново» is hidden, «Обновить» still works. */
  canAsk: boolean;
  busy: boolean;
  refreshing: boolean;
  onRefresh?: () => void;
  onAskAgain?: () => void;
}

/** Says the answer is a saved one and offers to recount it (no model) or to ask the model again. */
export function SavedAnswerBar({ saved, locale, canAsk, busy, refreshing, onRefresh, onAskAgain }: SavedAnswerBarProps) {
  const { t } = useTranslation();
  const message = saved.origin === "reused"
    ? t("analytics.history.reused")
    : t("analytics.history.saved", { date: formatMoment(saved.answeredAt, locale) });
  return (
    <Surface tone="soft" className={styles.savedBar} aria-label={t("analytics.history.savedLabel")} as="section">
      <p className={styles.savedText}>
        <History aria-hidden="true" size={16} />
        <span>{message}</span>
      </p>
      <div className={styles.savedActions}>
        {saved.origin === "history" && onRefresh && (
          <Button intent="secondary" loading={refreshing} disabled={busy && !refreshing} onClick={onRefresh}
            leftSection={<RefreshCw aria-hidden="true" size={15} />}>
            {t("analytics.history.refresh")}
          </Button>
        )}
        {canAsk && !saved.isFollowUp && onAskAgain && (
          <Button intent="quiet" disabled={busy} onClick={onAskAgain} leftSection={<Sparkles aria-hidden="true" size={15} />}>
            {t("analytics.history.askAgain")}
          </Button>
        )}
      </div>
    </Surface>
  );
}

export interface AnalyticsHistoryListProps {
  items: AnalyticsHistoryItem[];
  locale: string;
  activeId?: string | null;
  hasMore?: boolean;
  failed?: boolean;
  /** The entry being opened or deleted, shown as busy. */
  busyId?: string | null;
  onOpen: (id: string) => void;
  onDelete: (id: string) => void;
  onShowMore?: () => void;
}

/** «Предыдущие вопросы»: the household's saved questions, newest answer first. */
export function AnalyticsHistoryList({
  items,
  locale,
  activeId,
  hasMore,
  failed,
  busyId,
  onOpen,
  onDelete,
  onShowMore
}: AnalyticsHistoryListProps) {
  const { t } = useTranslation();
  const titleId = useId();
  const [confirming, setConfirming] = useState<string | null>(null);
  // The confirm buttons vanish with the deleted row, so focus moves to the heading instead of being lost.
  const headingRef = useRef<HTMLHeadingElement>(null);

  if (items.length === 0 && !failed) return null;

  return (
    <Surface as="section" className={styles.history} aria-labelledby={titleId}>
      <div className={styles.header}>
        <h2 id={titleId} className={styles.title} ref={headingRef} tabIndex={-1}>
          <History aria-hidden="true" size={18} />{t("analytics.history.title")}
        </h2>
        <HelpLink topic="analytics-history" />
      </div>
      {failed && <p className={styles.failed} role="alert">{t("analytics.history.loadFailed")}</p>}
      {items.length > 0 && (
        <ul className={styles.list}>
          {items.map((item) => {
            const active = item.id === activeId;
            const busy = item.id === busyId;
            const asker = item.askedByMe
              ? t("analytics.history.askedByMe")
              : item.askedByEmail ?? t("analytics.history.askedBySomeone");
            return (
              <li key={item.id} className={`${styles.item} ${active ? styles.active : ""}`}>
                <button type="button" className={styles.open} aria-current={active ? "true" : undefined}
                  aria-busy={busy || undefined} disabled={busy} onClick={() => onOpen(item.id)}>
                  <span className={styles.question}>
                    {item.isFollowUp && <MessageSquareReply aria-hidden="true" size={14} className={styles.followUpIcon} />}
                    {item.question}
                  </span>
                  {item.conclusion && <span className={styles.conclusion}>{item.conclusion}</span>}
                  <span className={styles.meta}>
                    {item.isFollowUp && <span className={styles.badge}>{t("analytics.history.followUp")}</span>}
                    <span>{formatMoment(item.answeredAt, locale)}</span>
                    <span aria-hidden="true">·</span>
                    <span className={styles.asker} title={asker}>{asker}</span>
                  </span>
                </button>
                <div className={styles.actions}>
                  {confirming === item.id
                    ? (
                      <div className={styles.confirm} role="group" aria-label={t("analytics.history.deleteConfirm")}>
                        <span>{t("analytics.history.deleteConfirm")}</span>
                        <Button intent="danger" loading={busy} onClick={() => {
                          setConfirming(null);
                          onDelete(item.id);
                          headingRef.current?.focus();
                        }}>
                          {t("analytics.history.deleteYes")}
                        </Button>
                        <Button intent="quiet" onClick={() => setConfirming(null)}>{t("analytics.history.cancel")}</Button>
                      </div>
                    )
                    : (
                      <IconButton disabled={busy}
                        label={t("analytics.history.deleteLabel", { question: item.question })}
                        tooltip={t("analytics.history.delete")}
                        icon={<Trash2 size={16} />}
                        onClick={() => setConfirming(item.id)} />
                    )}
                </div>
              </li>
            );
          })}
        </ul>
      )}
      {hasMore && onShowMore && (
        <Button intent="quiet" onClick={onShowMore}>{t("analytics.history.more")}</Button>
      )}
    </Surface>
  );
}
