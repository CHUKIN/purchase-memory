import { IonButton, IonCheckbox, IonItem, IonLabel, IonList, IonSpinner } from "@ionic/react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Sparkles } from "lucide-react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { ApiError, getCategorizationPending, runCategorizationBackfill } from "../../api/client";
import type { CategorizationRunSummary } from "../../api/types";
import { Footnote, Notice, SectionHeader } from "../../design-system/components";
import { HelpLink } from "../help/HelpLink";
import { reviewQueueCountQueryKey, reviewQueueQueryKey } from "../review-queue/reviewQueueQueries";
import { brandsQueryKey, categoriesQueryKey } from "./categoryQueries";

/** Products per press; the server accepts up to 500, 100 keeps one run short and cheap. */
export const backfillLimit = 100;

export const categorizationPendingQueryKey = ["categorization", "pending"] as const;

/**
 * «Дозаполнить названия и категории» in «Ещё»: the paid AI backfill that used
 * to be run from the browser console. The run is a mutation, never a query, so
 * nothing (a focus, a session refresh that invalidates every query) can repeat
 * it on its own; it needs an explicit confirm and is disabled while running.
 */
export function BackfillSection() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [expanded, setExpanded] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [retry, setRetry] = useState(false);

  // Free read; only asked for once the person opens the section.
  const pending = useQuery({
    queryKey: categorizationPendingQueryKey,
    queryFn: getCategorizationPending,
    enabled: expanded,
    retry: false
  });

  const run = useMutation({
    mutationFn: (withRetry: boolean) => runCategorizationBackfill(backfillLimit, withRetry),
    onSettled: () => {
      setConfirming(false);
    },
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["products"] }),
        queryClient.invalidateQueries({ queryKey: categoriesQueryKey }),
        queryClient.invalidateQueries({ queryKey: brandsQueryKey }),
        queryClient.invalidateQueries({ queryKey: reviewQueueQueryKey }),
        queryClient.invalidateQueries({ queryKey: reviewQueueCountQueryKey }),
        queryClient.invalidateQueries({ queryKey: categorizationPendingQueryKey })
      ]);
    }
  });

  const counts = pending.data;
  const total = counts ? (retry ? counts.waitingWithRetry : counts.waiting) : 0;
  const toRun = Math.min(total, backfillLimit);
  const retryExtra = counts ? Math.max(0, counts.waitingWithRetry - counts.waiting) : 0;
  const running = run.isPending;

  const start = () => {
    if (running || toRun === 0) return;
    run.mutate(retry);
  };

  return (
    <>
      <SectionHeader level={3} action={<HelpLink topic="product-names" />}>{t("backfill.title")}</SectionHeader>
      <IonList inset className="pm-after-header">
        <IonItem
          button
          detail={false}
          lines={expanded ? undefined : "none"}
          aria-expanded={expanded}
          onClick={() => setExpanded((value) => !value)}
        >
          <Sparkles slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
          <IonLabel className="pm-wrap">{t("backfill.open")}</IonLabel>
        </IonItem>

        {expanded && (
          <div className="ion-padding">
            {pending.isPending && <Footnote role="status">{t("backfill.loading")}</Footnote>}
            {pending.isError && (
              <Notice tone="danger" role="alert">
                <p>{t("backfill.pendingFailed")}</p>
                <IonButton size="small" fill="outline" onClick={() => void pending.refetch()}>{t("ui.reload")}</IonButton>
              </Notice>
            )}

            {counts && (
              <>
                <p>
                  {total === 0
                    ? t("backfill.nothingWaiting")
                    : t("backfill.waiting", { count: total, unclassified: counts.unclassified, unnamed: counts.unnamed })}
                </p>
                {retryExtra > 0 && (
                  <IonCheckbox
                    checked={retry}
                    disabled={running}
                    justify="start"
                    labelPlacement="end"
                    onIonChange={(event) => { setRetry(event.detail.checked); setConfirming(false); }}
                  >
                    <span className="pm-wrap">{t("backfill.retry", { count: retryExtra })}</span>
                  </IonCheckbox>
                )}
                <Footnote>{t("backfill.cost", { limit: backfillLimit })}</Footnote>

                {!confirming && !running && (
                  <IonButton expand="block" shape="round" disabled={toRun === 0} onClick={() => { run.reset(); setConfirming(true); }}>
                    {t("backfill.run")}
                  </IonButton>
                )}
                {confirming && !running && (
                  <Notice role="alert">
                    <p>{t("backfill.confirm", { count: toRun })}</p>
                    <IonButton expand="block" shape="round" onClick={start}>{t("backfill.confirmYes")}</IonButton>
                    <IonButton expand="block" shape="round" fill="clear" onClick={() => setConfirming(false)}>{t("ui.cancel")}</IonButton>
                  </Notice>
                )}
                {running && (
                  <>
                    <div role="status" aria-live="polite" className="pm-wrap">
                      <IonSpinner name="dots" /> {t("backfill.running")}
                    </div>
                    <IonButton expand="block" shape="round" disabled>{t("backfill.run")}</IonButton>
                  </>
                )}
              </>
            )}

            {run.isSuccess && <RunResult result={run.data} />}
            {run.isError && (
              <Notice tone="danger" role="alert"><p>{failureText(t, run.error)}</p></Notice>
            )}
          </div>
        )}
      </IonList>
    </>
  );
}

function RunResult({ result }: { result: CategorizationRunSummary }) {
  const { t } = useTranslation();
  if (!result.configured) {
    return <Notice tone="warning" role="status"><p>{t("backfill.notConfigured")}</p></Notice>;
  }
  const names = result.namesSuggested ?? 0;
  return (
    <Notice tone={result.failedBatches > 0 ? "warning" : "success"} role="status">
      <p>{t("backfill.resultConsidered", { count: result.considered })}</p>
      <p>{t("backfill.resultSuggested", { count: result.suggested, names })}</p>
      {result.failedBatches > 0 && <p>{t("backfill.resultFailed", { count: result.failedBatches })}</p>}
      <p>{t("backfill.resultCheck")}</p>
    </Notice>
  );
}

function failureText(t: (key: string) => string, error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 409) return t("backfill.alreadyRunning");
    if (error.status === 429) return t("backfill.tooOften");
  }
  return t("backfill.failed");
}
