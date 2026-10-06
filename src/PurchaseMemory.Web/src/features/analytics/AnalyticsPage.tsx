import { useMutation, useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { ApiError, askAnalytics, getAnalyticsContext, listCategories, listTags, runAnalyticsPlan } from "../../api/client";
import type { AnalyticsAnswer, AnalyticsPlan } from "../../api/types";
import { categoriesQueryKey } from "../categories/categoryQueries";
import { tagsQueryKey } from "../tags/tagQueries";
import { AnalyticsView, type AnalyticsProblem } from "./AnalyticsView";

function problemOf(error: unknown): AnalyticsProblem {
  if (error instanceof ApiError) {
    if (error.status === 429) return { kind: "rateLimited", retryAfterSeconds: error.retryAfterSeconds };
    if (error.status === 503 || error.code === "analyticsDisabled") return { kind: "disabled" };
    if (error.status === 422) return { kind: "notUnderstood" };
  }
  return { kind: "failed" };
}

/**
 * Questions about the purchase history. A question goes to the model once and
 * comes back as a plan; every later correction — removing a chip, changing the
 * period — re-runs that plan on the server without the model. The next
 * question refines the current answer until the user starts over.
 */
export function AnalyticsPage() {
  const { i18n } = useTranslation();
  const language = i18n.resolvedLanguage === "en" ? "en" : "ru";
  const locale = language === "en" ? "en-GB" : "ru-RU";

  const contextQuery = useQuery({ queryKey: ["analytics", "context"], queryFn: getAnalyticsContext });
  const categoriesQuery = useQuery({ queryKey: categoriesQueryKey, queryFn: listCategories });
  const tagsQuery = useQuery({ queryKey: tagsQueryKey, queryFn: listTags });

  const [question, setQuestion] = useState("");
  const [answer, setAnswer] = useState<AnalyticsAnswer | null>(null);
  const [followUp, setFollowUp] = useState<AnalyticsPlan | null>(null);
  const [problem, setProblem] = useState<AnalyticsProblem | null>(null);

  const accept = (next: AnalyticsAnswer) => {
    setAnswer(next);
    setFollowUp(next.plan);
    setProblem(null);
  };

  const ask = useMutation({
    mutationFn: (text: string) => askAnalytics(text, followUp, language),
    onSuccess: (next) => {
      accept(next);
      setQuestion("");
    },
    onError: (error) => setProblem(problemOf(error))
  });

  const run = useMutation({
    mutationFn: (plan: AnalyticsPlan) => runAnalyticsPlan(plan, language),
    onSuccess: accept,
    onError: (error) => setProblem(problemOf(error))
  });

  const disabledByServer = problem?.kind === "disabled";
  return (
    <AnalyticsView
      enabled={(contextQuery.data?.enabled ?? !contextQuery.isError) && !disabledByServer}
      question={question}
      onQuestionChange={setQuestion}
      onAsk={(text) => ask.mutate(text)}
      asking={ask.isPending}
      rerunning={run.isPending}
      problem={problem}
      answer={answer}
      followUp={followUp}
      onClearFollowUp={() => setFollowUp(null)}
      onPlanChange={(plan) => run.mutate(plan)}
      categories={categoriesQuery.data ?? []}
      merchants={contextQuery.data?.merchants ?? []}
      tags={tagsQuery.data ?? []}
      locale={locale}
    />
  );
}
