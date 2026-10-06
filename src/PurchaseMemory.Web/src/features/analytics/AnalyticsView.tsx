import { AlertTriangle, CornerDownRight, MessageSquareText, Search, X } from "lucide-react";
import { useEffect, useId, useRef, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import type { AnalyticsAnswer, AnalyticsPlan, ProductCategory, ProductTag } from "../../api/types";
import { Button, EmptyState, SkeletonCard, Surface } from "../../design-system/components";
import { periodLabel } from "./analyticsText";
import { AnswerCard } from "./AnswerCard";
import { HelpLink } from "../help/HelpLink";
import styles from "./AnalyticsView.module.css";

export type AnalyticsProblem =
  | { kind: "rateLimited"; retryAfterSeconds: number | null }
  | { kind: "disabled" }
  | { kind: "notUnderstood" }
  | { kind: "failed" };

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
}

const exampleKeys = ["milkYear", "coffeeBrandPrice", "sweets", "wholegrainPasta", "biedronkaAugust"] as const;

/** The Analytics page without data fetching: the question form, its states and the answer. */
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
  locale
}: AnalyticsViewProps) {
  const { t } = useTranslation();
  const inputId = useId();
  const conclusionRef = useRef<HTMLHeadingElement>(null);
  // Starts at the answer shown on first render, so only answers that arrive later move focus.
  const shownAnswer = useRef<AnalyticsAnswer | null>(answer);

  // A new answer takes focus, so keyboard and screen-reader users land on it.
  useEffect(() => {
    if (answer && answer !== shownAnswer.current) conclusionRef.current?.focus();
    shownAnswer.current = answer;
  }, [answer]);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const trimmed = question.trim();
    if (trimmed && !asking) onAsk(trimmed);
  };

  const status = asking
    ? t("analytics.status.asking")
    : rerunning
      ? t("analytics.status.rerunning")
      : answer
        ? t("analytics.status.ready")
        : "";

  return (
    <div className={styles.page}>
      <header className={styles.hero}>
        <p className={styles.eyebrow}>{t("analytics.eyebrow")}</p>
        <h1>{t("analytics.title")}</h1>
        <p className={styles.intro}>{t("analytics.intro")}</p>
        <HelpLink topic="analytics" />
      </header>

      <Surface className={styles.askCard}>
        <form className={styles.form} onSubmit={submit} role="search" aria-label={t("analytics.formLabel")}>
          {followUp && (
            <div className={styles.followUp}>
              <CornerDownRight aria-hidden="true" size={16} />
              <span>{t("analytics.followUp.label", { period: periodLabel(t, locale, followUp) })}</span>
              <button type="button" className={styles.clearFollowUp} onClick={onClearFollowUp}>
                <X aria-hidden="true" size={14} />{t("analytics.followUp.clear")}
              </button>
            </div>
          )}
          <label htmlFor={inputId} className={styles.label}>
            {followUp ? t("analytics.followUp.inputLabel") : t("analytics.inputLabel")}
          </label>
          <div className={styles.inputRow}>
            <textarea
              id={inputId}
              className={styles.input}
              value={question}
              maxLength={500}
              rows={2}
              placeholder={t("analytics.placeholder")}
              disabled={!enabled}
              onChange={(event) => onQuestionChange(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === "Enter" && !event.shiftKey) submit(event);
              }}
            />
            <Button type="submit" loading={asking} disabled={!enabled || !question.trim()} leftSection={<Search aria-hidden="true" size={16} />}>
              {t("analytics.ask")}
            </Button>
          </div>
          {enabled && !followUp && (
            <div className={styles.examples}>
              <span id={`${inputId}-examples`} className={styles.examplesLabel}>{t("analytics.examplesLabel")}</span>
              <ul aria-labelledby={`${inputId}-examples`}>
                {exampleKeys.map((key) => (
                  <li key={key}>
                    <button type="button" className={styles.example} disabled={asking} onClick={() => {
                      const text = t(`analytics.examples.${key}`);
                      onQuestionChange(text);
                      onAsk(text);
                    }}>
                      {t(`analytics.examples.${key}`)}
                    </button>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </form>
        {!enabled && (
          <p className={styles.disabled} role="note">{t("analytics.disabled")}</p>
        )}
      </Surface>

      <div aria-live="polite" className="visually-hidden">{status}</div>

      {problem && <ProblemNotice problem={problem} />}

      {asking && !answer && (
        <div aria-busy="true"><div aria-hidden="true"><SkeletonCard /></div></div>
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
          rerunning={rerunning || asking}
        />
      )}

      {!answer && !asking && !problem && enabled && (
        <Surface className={styles.empty}>
          <EmptyState icon={<MessageSquareText />} title={t("analytics.emptyTitle")} description={t("analytics.emptyDescription")} />
        </Surface>
      )}
    </div>
  );
}

function ProblemNotice({ problem }: { problem: AnalyticsProblem }) {
  const { t } = useTranslation();
  const message = problem.kind === "rateLimited"
    ? problem.retryAfterSeconds
      ? t("analytics.problems.rateLimitedIn", { minutes: Math.max(1, Math.ceil(problem.retryAfterSeconds / 60)) })
      : t("analytics.problems.rateLimited")
    : t(`analytics.problems.${problem.kind}`);
  return (
    <Surface className={styles.problem} role="alert">
      <AlertTriangle aria-hidden="true" size={18} />
      <p>{message}</p>
    </Surface>
  );
}
