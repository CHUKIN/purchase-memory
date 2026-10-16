import { IonActionSheet, IonButton, IonItem, IonLabel, IonList } from "@ionic/react";
import { MessageSquareReply, RefreshCw, Sparkles, Trash2 } from "lucide-react";
import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import type { AnalyticsHistoryItem } from "../../api/types";
import { Footnote, Notice } from "../../design-system/components";
import { HelpLink } from "../help/HelpLink";

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
  const showRefresh = saved.origin === "history" && onRefresh;
  const showAskAgain = canAsk && !saved.isFollowUp && onAskAgain;
  return (
    <section aria-label={t("analytics.history.savedLabel")}>
      <Notice><p>{message}</p></Notice>
      {(showRefresh || showAskAgain) && (
        <IonList inset>
          {showRefresh && (
            <IonItem button detail={false} disabled={busy && !refreshing} lines={showAskAgain ? undefined : "none"} onClick={onRefresh}>
              <RefreshCw slot="start" size={18} className="pm-row-icon" aria-hidden="true" />
              <IonLabel color="primary">{t("analytics.history.refresh")}</IonLabel>
            </IonItem>
          )}
          {showAskAgain && (
            <IonItem button detail={false} disabled={busy} lines="none" onClick={onAskAgain}>
              <Sparkles slot="start" size={18} className="pm-row-icon" aria-hidden="true" />
              <IonLabel color="primary">{t("analytics.history.askAgain")}</IonLabel>
            </IonItem>
          )}
        </IonList>
      )}
    </section>
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
  const [confirming, setConfirming] = useState<string | null>(null);
  // The confirm sheet goes away with the deleted row, so focus moves to the heading instead of being lost.
  const headingRef = useRef<HTMLHeadingElement>(null);

  if (items.length === 0 && !failed) return null;

  return (
    <section aria-labelledby="analytics-history-title">
      <div className="pm-section-header">
        <h2 id="analytics-history-title" ref={headingRef} tabIndex={-1}>{t("analytics.history.title")}</h2>
        <HelpLink topic="analytics-history" />
      </div>
      {failed && <Footnote tone="danger" role="alert">{t("analytics.history.loadFailed")}</Footnote>}
      {items.length > 0 && (
        <IonList inset className="pm-after-header" role="list">
          {items.map((item, index) => {
            const active = item.id === activeId;
            const busy = item.id === busyId;
            const asker = item.askedByMe
              ? t("analytics.history.askedByMe")
              : item.askedByEmail ?? t("analytics.history.askedBySomeone");
            return (
              <div role="listitem" key={item.id} className={active ? "pm-history-active" : undefined}>
                <IonItem lines={index === items.length - 1 ? "none" : undefined}>
                  <IonButton
                    fill="clear"
                    className="pm-history-open"
                    aria-current={active ? "true" : undefined}
                    aria-busy={busy || undefined}
                    disabled={busy}
                    onClick={() => onOpen(item.id)}
                  >
                    <span className="pm-history-text">
                      <span className="pm-history-question">
                        {item.isFollowUp && <MessageSquareReply aria-hidden="true" size={14} />}
                        {item.question}
                      </span>
                      {item.conclusion && <span className="pm-history-conclusion">{item.conclusion}</span>}
                      <span className="pm-history-meta">
                        {/* The ↪ before the question marks a follow-up; the word is for screen readers. */}
                        {item.isFollowUp && <span className="visually-hidden">{t("analytics.history.followUp")}</span>}
                        <span>{formatMoment(item.answeredAt, locale)}</span>
                        <span aria-hidden="true">·</span>
                        <span title={asker}>{asker}</span>
                      </span>
                    </span>
                  </IonButton>
                  <IonButton
                    slot="end"
                    fill="clear"
                    color="medium"
                    className="pm-icon-button"
                    disabled={busy}
                    aria-label={t("analytics.history.deleteLabel", { question: item.question })}
                    onClick={() => setConfirming(item.id)}
                  >
                    <Trash2 size={18} aria-hidden="true" />
                  </IonButton>
                </IonItem>
              </div>
            );
          })}
        </IonList>
      )}
      {hasMore && onShowMore && (
        <div className="ion-padding-horizontal">
          <IonButton expand="block" fill="clear" onClick={onShowMore}>{t("analytics.history.more")}</IonButton>
        </div>
      )}
      <IonActionSheet
        isOpen={confirming !== null}
        header={t("analytics.history.deleteConfirm")}
        onDidDismiss={() => setConfirming(null)}
        buttons={[
          {
            text: t("analytics.history.deleteYes"),
            role: "destructive",
            handler: () => {
              if (confirming) onDelete(confirming);
              headingRef.current?.focus();
            }
          },
          { text: t("analytics.history.cancel"), role: "cancel" }
        ]}
      />
    </section>
  );
}
