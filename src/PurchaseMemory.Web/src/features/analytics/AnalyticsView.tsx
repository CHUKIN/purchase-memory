import { IonButton, IonItem, IonLabel, IonList, IonTextarea } from "@ionic/react";
import { CornerDownRight, MessageSquareText, Search, X } from "lucide-react";
import { useEffect, useRef, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import type { AnalyticsAnswer, AnalyticsHistoryItem, AnalyticsPlan, ProductCategory, ProductTag } from "../../api/types";
import { ActionButton, EmptyState, Footnote, LoadingList, Notice, Page, SectionHeader } from "../../design-system/components";
import { HelpLink } from "../help/HelpLink";
import { AnalyticsHistoryList, SavedAnswerBar, type SavedAnswerInfo } from "./AnalyticsHistory";
import { periodLabel } from "./analyticsText";
import { AnswerCard } from "./AnswerCard";

export type AnalyticsProblem =
  | { kind: "rateLimited"; retryAfterSeconds: number | null }
  | { kind: "disabled" }
  | { kind: "notUnderstood" }
  | { kind: "failed" }
  /** Opening, refreshing or deleting a saved question failed. */
  | { kind: "historyFailed" }
  /** The saved question was written by an older version and cannot be recounted. */
  | { kind: "historyOutdated" };

export interface AnalyticsViewProps {
  /** False when the server has questions switched off; chips on an existing answer still work. */
  enabled: boolean;
  question: string;
  onQuestionChange: (question: string) => void;
  /** Asks the typed question; a follow-up when `followUp` is set. */
  onAsk: (question: string) => void;
  asking: boolean;
  rerunning: boolean;
  problem: AnalyticsProblem | null;
  answer: AnalyticsAnswer | null;
  /** The previous answer's plan the next question refines, if the user keeps it. */
  followUp: AnalyticsPlan | null;
  onClearFollowUp: () => void;
  onPlanChange: (plan: AnalyticsPlan) => void;
  categories: ProductCategory[];
  merchants: string[];
  /** The household's tags; the tag field appears only when there are some. */
  tags?: ProductTag[];
  locale: string;
  /** «Предыдущие вопросы»; the section is hidden while empty. */
  history?: AnalyticsHistoryItem[];
  historyHasMore?: boolean;
  historyFailed?: boolean;
  /** The saved question being opened or deleted. */
  historyBusyId?: string | null;
  onOpenHistory?: (id: string) => void;
  onDeleteHistory?: (id: string) => void;
  onShowMoreHistory?: () => void;
  /** Set when the answer on screen is a saved one (opened from the list, or the same question asked again). */
  saved?: SavedAnswerInfo | null;
  /** The history entry the current answer belongs to, highlighted in the list. */
  activeHistoryId?: string | null;
  /** «Обновить»: recount the saved plan on today's data, without the model. */
  onRefreshSaved?: () => void;
  refreshing?: boolean;
  /** «Спросить заново»: send the saved question to the model again. */
  onAskAgain?: () => void;
}

const exampleKeys = ["milkYear", "coffeeBrandPrice", "sweets", "wholegrainPasta", "biedronkaAugust"] as const;

/** The Analytics tab without data fetching: the question box, its states and the answer. */
export function AnalyticsView({
  enabled,
  question,
  onQuestionChange,
  onAsk,
  asking,
  rerunning,
  problem,
  answer,
  followUp,
  onClearFollowUp,
  onPlanChange,
  categories,
  merchants,
  tags = [],
  locale,
  history = [],
  historyHasMore = false,
  historyFailed = false,
  historyBusyId = null,
  onOpenHistory,
  onDeleteHistory,
  onShowMoreHistory,
  saved = null,
  activeHistoryId = null,
  onRefreshSaved,
  refreshing = false,
  onAskAgain
}: AnalyticsViewProps) {
  const { t } = useTranslation();
  const conclusionRef = useRef<HTMLHeadingElement>(null);
  // Starts at the answer shown on first render, so only answers that arrive later move focus.
  const shownAnswer = useRef<AnalyticsAnswer | null>(answer);

  // A new answer takes focus, so keyboard and screen-reader users land on it.
  useEffect(() => {
    if (answer && answer !== shownAnswer.current) conclusionRef.current?.focus();
    shownAnswer.current = answer;
  }, [answer]);

  const submit = (event?: FormEvent) => {
    event?.preventDefault();
    const trimmed = question.trim();
    if (trimmed && !asking) onAsk(trimmed);
  };

  const status = asking
    ? t("analytics.status.asking")
    : refreshing
      ? t("analytics.status.refreshing")
      : rerunning
        ? t("analytics.status.rerunning")
        : answer
          ? t("analytics.status.ready")
          : "";

  return (
    <Page title={t("common.analytics")} end={<HelpLink topic="analytics" />}>

      <form onSubmit={submit} role="search" aria-label={t("analytics.formLabel")}>
        <IonList inset>
          {followUp && (
            <IonItem>
              <CornerDownRight slot="start" size={18} className="pm-row-icon" aria-hidden="true" />
              <IonLabel className="pm-wrap"><p>{t("analytics.followUp.label", { period: periodLabel(t, locale, followUp) })}</p></IonLabel>
              <IonButton slot="end" fill="clear" color="medium" className="pm-icon-button" onClick={onClearFollowUp} aria-label={t("analytics.followUp.clear")}>
                <X size={20} aria-hidden="true" />
              </IonButton>
            </IonItem>
          )}
          <IonItem lines="none">
            <IonTextarea
              label={followUp ? t("analytics.followUp.inputLabel") : t("analytics.inputLabel")}
              labelPlacement="stacked"
              value={question}
              maxlength={500}
              rows={2}
              autoGrow
              placeholder={t("analytics.placeholder")}
              disabled={!enabled}
              enterkeyhint="search"
              onIonInput={(event) => onQuestionChange(String(event.detail.value ?? ""))}
              onKeyDown={(event) => {
                if (event.key === "Enter" && !event.shiftKey) { event.preventDefault(); submit(); }
              }}
            />
          </IonItem>
        </IonList>
        <div className="pm-primary-block">
          <ActionButton type="submit" expand="block" shape="round" loading={asking} disabled={!enabled || !question.trim()}>
            <Search aria-hidden="true" size={18} />&nbsp;{t("analytics.ask")}
          </ActionButton>
        </div>
      </form>
      {!enabled && <Footnote tone="warning" role="note">{t("analytics.disabled")}</Footnote>}

      {enabled && !followUp && !answer && (
        <section aria-labelledby="analytics-examples">
          <SectionHeader id="analytics-examples">{t("analytics.examplesLabel")}</SectionHeader>
          <IonList inset className="pm-after-header">
            {exampleKeys.map((key, index) => (
              <IonItem key={key} button detail={false} disabled={asking} lines={index === exampleKeys.length - 1 ? "none" : undefined} onClick={() => {
                const text = t(`analytics.examples.${key}`);
                onQuestionChange(text);
                onAsk(text);
              }}>
                <IonLabel className="pm-wrap" color="primary">{t(`analytics.examples.${key}`)}</IonLabel>
              </IonItem>
            ))}
          </IonList>
        </section>
      )}

      <div aria-live="polite" className="visually-hidden">{status}</div>

      {problem && <ProblemNotice problem={problem} />}

      {asking && !answer && <LoadingList label={t("analytics.status.asking")} rows={2} />}

      {answer && saved && (
        <SavedAnswerBar
          saved={saved}
          locale={locale}
          canAsk={enabled}
          busy={asking || rerunning || refreshing}
          refreshing={refreshing}
          onRefresh={onRefreshSaved}
          onAskAgain={onAskAgain}
        />
      )}

      {answer && (
        <AnswerCard
          ref={conclusionRef}
          answer={answer}
          categories={categories}
          merchants={merchants}
          tags={tags}
          locale={locale}
          onPlanChange={onPlanChange}
          rerunning={rerunning || asking || refreshing}
        />
      )}

      {!answer && !asking && !problem && enabled && history.length === 0 && (
        <EmptyState icon={<MessageSquareText />} title={t("analytics.emptyTitle")} />
      )}

      {onOpenHistory && onDeleteHistory && (
        <AnalyticsHistoryList
          items={history}
          locale={locale}
          activeId={activeHistoryId}
          hasMore={historyHasMore}
          failed={historyFailed}
          busyId={historyBusyId}
          onOpen={onOpenHistory}
          onDelete={onDeleteHistory}
          onShowMore={onShowMoreHistory}
        />
      )}
    </Page>
  );
}

function ProblemNotice({ problem }: { problem: AnalyticsProblem }) {
  const { t } = useTranslation();
  const message = problem.kind === "rateLimited"
    ? problem.retryAfterSeconds
      ? t("analytics.problems.rateLimitedIn", { minutes: Math.max(1, Math.ceil(problem.retryAfterSeconds / 60)) })
      : t("analytics.problems.rateLimited")
    : t(`analytics.problems.${problem.kind}`);
  return <Notice tone="warning" role="alert"><p>{message}</p></Notice>;
}
