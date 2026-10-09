import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  ApiError,
  askAnalytics,
  deleteAnalyticsHistoryEntry,
  getAnalyticsContext,
  getAnalyticsHistoryEntry,
  listAnalyticsHistory,
  listCategories,
  listTags,
  refreshAnalyticsHistoryEntry,
  runAnalyticsPlan
} from "../../api/client";
import type { AnalyticsAnswer, AnalyticsHistoryDetail, AnalyticsPlan } from "../../api/types";
import { categoriesQueryKey } from "../categories/categoryQueries";
import { tagsQueryKey } from "../tags/tagQueries";
import type { SavedAnswerInfo } from "./AnalyticsHistory";
import { AnalyticsView, type AnalyticsProblem } from "./AnalyticsView";

export const analyticsHistoryQueryKey = ["analytics", "history"] as const;

const historyPageSize = 20;
/** The server's largest page; the list grows by «Показать ещё» up to it. */
const historyMaximum = 100;

function problemOf(error: unknown): AnalyticsProblem {
  if (error instanceof ApiError) {
    if (error.status === 429) return { kind: "rateLimited", retryAfterSeconds: error.retryAfterSeconds };
    if (error.status === 503 || error.code === "analyticsDisabled") return { kind: "disabled" };
    if (error.status === 422) return { kind: "notUnderstood" };
  }
  return { kind: "failed" };
}

function historyProblemOf(error: unknown): AnalyticsProblem {
  return error instanceof ApiError && error.code === "historyEntryOutdated"
    ? { kind: "historyOutdated" }
    : { kind: "historyFailed" };
}

interface AskInput {
  /** See `latestRequest`. */
  token: number;
  text: string;
  previousPlan: AnalyticsPlan | null;
  /** «Спросить заново»: go to the model even if the question was asked before. */
  askModel?: boolean;
  /** Typed in the box (or an example): the box is cleared once answered. */
  fromInput?: boolean;
}

/**
 * Questions about the purchase history. A question goes to the model once and
 * comes back as a plan; every later correction — removing a chip, changing the
 * period — re-runs that plan on the server without the model. The next
 * question refines the current answer until the user starts over.
 * Every asked question is kept in «Предыдущие вопросы»: opening one shows its
 * saved answer, «Обновить» recounts it without the model, and asking the same
 * words again reuses the saved plan on the server.
 */
export function AnalyticsPage() {
  const { i18n } = useTranslation();
  const language = i18n.resolvedLanguage === "en" ? "en" : "ru";
  const locale = language === "en" ? "en-GB" : "ru-RU";
  const queryClient = useQueryClient();

  const contextQuery = useQuery({ queryKey: ["analytics", "context"], queryFn: getAnalyticsContext });
  const categoriesQuery = useQuery({ queryKey: categoriesQueryKey, queryFn: listCategories });
  const tagsQuery = useQuery({ queryKey: tagsQueryKey, queryFn: listTags });
  const [historyLimit, setHistoryLimit] = useState(historyPageSize);
  const historyQuery = useQuery({
    queryKey: [...analyticsHistoryQueryKey, historyLimit],
    queryFn: () => listAnalyticsHistory(historyLimit),
    placeholderData: (previous) => previous
  });

  const [question, setQuestion] = useState("");
  const [answer, setAnswer] = useState<AnalyticsAnswer | null>(null);
  const [followUp, setFollowUp] = useState<AnalyticsPlan | null>(null);
  const [problem, setProblem] = useState<AnalyticsProblem | null>(null);
  const [saved, setSaved] = useState<SavedAnswerInfo | null>(null);
  const [activeHistoryId, setActiveHistoryId] = useState<string | null>(null);

  // Asking, opening, refreshing and re-running all replace the answer on
  // screen. Only the most recently started one may do so: a slow earlier
  // request that finishes last must not overwrite the newer answer.
  const latestRequest = useRef(0);
  const startRequest = () => ++latestRequest.current;
  const isLatest = (token: number) => token === latestRequest.current;

  const refreshHistory = () => queryClient.invalidateQueries({ queryKey: analyticsHistoryQueryKey });

  const accept = (next: AnalyticsAnswer) => {
    setAnswer(next);
    setFollowUp(next.plan);
    setProblem(null);
  };

  const showSaved = (detail: AnalyticsHistoryDetail, origin: SavedAnswerInfo["origin"]) => {
    if (detail.answer) accept(detail.answer);
    setActiveHistoryId(detail.entry.id);
    setSaved({
      entryId: detail.entry.id,
      question: detail.entry.question,
      answeredAt: detail.entry.answeredAt,
      origin,
      isFollowUp: detail.entry.isFollowUp
    });
  };

  const ask = useMutation({
    mutationFn: ({ text, previousPlan, askModel }: AskInput) => askAnalytics(text, previousPlan, language, askModel === true),
    onSuccess: (next, { token, text, previousPlan, fromInput }) => {
      void refreshHistory();
      if (!isLatest(token)) return;
      accept(next);
      if (fromInput) setQuestion("");
      setActiveHistoryId(next.historyId);
      setSaved(next.reused && next.historyId
        ? { entryId: next.historyId, question: text, answeredAt: new Date().toISOString(), origin: "reused", isFollowUp: previousPlan !== null }
        : null);
    },
    onError: (error, { token }) => {
      if (isLatest(token)) setProblem(problemOf(error));
    }
  });

  const run = useMutation({
    mutationFn: ({ plan }: { token: number; plan: AnalyticsPlan }) => runAnalyticsPlan(plan, language),
    onSuccess: (next, { token }) => {
      if (!isLatest(token)) return;
      accept(next);
      // Edited filters make a new answer that is not the saved one any more.
      setSaved(null);
      setActiveHistoryId(null);
    },
    onError: (error, { token }) => {
      if (isLatest(token)) setProblem(problemOf(error));
    }
  });

  const open = useMutation({
    mutationFn: async ({ entryId }: { token: number; entryId: string }) => {
      const detail = await getAnalyticsHistoryEntry(entryId);
      // An entry written by an older, incompatible version is recounted right away.
      return detail.answer ? detail : refreshAnalyticsHistoryEntry(entryId, language);
    },
    onSuccess: (detail, { token }) => {
      if (!isLatest(token)) return;
      setProblem(null);
      showSaved(detail, "history");
    },
    onError: (error, { token }) => {
      if (isLatest(token)) setProblem(historyProblemOf(error));
    }
  });

  const refresh = useMutation({
    mutationFn: ({ entryId }: { token: number; entryId: string }) => refreshAnalyticsHistoryEntry(entryId, language),
    onSuccess: (detail, { token }) => {
      void refreshHistory();
      if (!isLatest(token)) return;
      setProblem(null);
      showSaved(detail, "history");
    },
    onError: (error, { token }) => {
      if (isLatest(token)) setProblem(historyProblemOf(error));
    }
  });

  const remove = useMutation({
    mutationFn: (entryId: string) => deleteAnalyticsHistoryEntry(entryId),
    onSuccess: (_, entryId) => {
      if (activeHistoryId === entryId) {
        setActiveHistoryId(null);
        setSaved(null);
      }
      void refreshHistory();
    },
    onError: (error, entryId) => {
      // Someone else already removed it: the list just catches up.
      if (error instanceof ApiError && error.status === 404) {
        void refreshHistory();
        if (activeHistoryId === entryId) {
          setActiveHistoryId(null);
          setSaved(null);
        }
        return;
      }
      setProblem({ kind: "historyFailed" });
    }
  });

  const disabledByServer = problem?.kind === "disabled";
  const historyBusyId = open.isPending ? open.variables.entryId : remove.isPending ? remove.variables : null;
  return (
    <AnalyticsView
      enabled={(contextQuery.data?.enabled ?? !contextQuery.isError) && !disabledByServer}
      question={question}
      onQuestionChange={setQuestion}
      onAsk={(text) => ask.mutate({ token: startRequest(), text, previousPlan: followUp, fromInput: true })}
      asking={ask.isPending}
      rerunning={run.isPending}
      problem={problem}
      answer={answer}
      followUp={followUp}
      onClearFollowUp={() => setFollowUp(null)}
      onPlanChange={(plan) => run.mutate({ token: startRequest(), plan })}
      categories={categoriesQuery.data ?? []}
      merchants={contextQuery.data?.merchants ?? []}
      tags={tagsQuery.data ?? []}
      locale={locale}
      history={historyQuery.data?.items ?? []}
      historyHasMore={(historyQuery.data?.hasMore ?? false) && historyLimit < historyMaximum}
      historyFailed={historyQuery.isError}
      historyBusyId={historyBusyId ?? null}
      onOpenHistory={(entryId) => open.mutate({ token: startRequest(), entryId })}
      onDeleteHistory={(entryId) => remove.mutate(entryId)}
      onShowMoreHistory={() => setHistoryLimit((limit) => Math.min(limit + historyPageSize, historyMaximum))}
      saved={saved}
      activeHistoryId={activeHistoryId}
      onRefreshSaved={saved ? () => refresh.mutate({ token: startRequest(), entryId: saved.entryId }) : undefined}
      refreshing={refresh.isPending}
      onAskAgain={saved ? () => ask.mutate({ token: startRequest(), text: saved.question, previousPlan: null, askModel: true }) : undefined}
    />
  );
}
