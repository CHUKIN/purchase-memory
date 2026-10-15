import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router";
import { uploadReceipt } from "../../api/client";
import type { ReceiptView, RecognitionStatus } from "../../api/types";
import { useAuth } from "../../auth/AuthContext";
import { Footnote, Page } from "../../design-system/components";
import { useBackTarget } from "../../app/backNavigation";
import { HelpLink } from "../help/HelpLink";
import { InterruptedBatchNotice, myUnsavedReceiptsQueryKey, RecentUploads } from "./BatchRecovery";
import { deviceBatchStore, type PendingBatchStore, type RestoredBatch } from "./pendingBatchStore";
import { ReceiptCapture, type CaptureMode } from "./ReceiptCapture";
import { ReceiptDuplicateNotice } from "./ReceiptDuplicateNotice";
import { messageOf } from "./receiptErrors";
import { useReceiptBatchUpload } from "./useReceiptBatchUpload";

/** `batchStore` is for tests and stories; the app keeps interrupted batches in IndexedDB. */
export function CapturePage({ recognition, batchStore = deviceBatchStore }: { recognition: RecognitionStatus | null; batchStore?: PendingBatchStore }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const back = useBackTarget({ to: "/", labelKey: "common.home" });
  const queryClient = useQueryClient();
  const [files, setFiles] = useState<File[]>([]);
  const [duplicate, setDuplicate] = useState<ReceiptView | null>(null);
  const [mode, setMode] = useState<CaptureMode>("parts");
  const userId = useAuth()?.user.id ?? null;
  const persistence = useMemo(() => ({ store: batchStore, userId }), [batchStore, userId]);
  // Each uploaded receipt refreshes the recent lists, so App starts polling and
  // notifies about every receipt of the batch, and «Недавние загрузки» shows it.
  const batch = useReceiptBatchUpload(() => {
    void queryClient.invalidateQueries({ queryKey: ["receipts", "recent"] });
    void queryClient.invalidateQueries({ queryKey: myUnsavedReceiptsQueryKey });
  }, persistence);
  const separate = mode === "separate" && files.length > 1;
  // A batch cut short by a reload, a killed PWA or leaving the screen.
  const [interrupted, setInterrupted] = useState<RestoredBatch | null>(null);
  useEffect(() => {
    let active = true;
    void batchStore.load(userId).then((restored) => {
      if (active && restored && restored.files.length + restored.lost.length > 0) setInterrupted(restored);
    });
    return () => { active = false; };
  }, [batchStore, userId]);
  function resumeInterrupted() {
    if (!interrupted) return;
    setFiles(interrupted.files);
    setMode("separate");
    upload.reset();
    setInterrupted(null);
    batch.start(interrupted.files);
  }
  function discardInterrupted() {
    setInterrupted(null);
    void batchStore.clear();
  }
  const upload = useMutation({
    // `process=true` queues recognition in the same request: one rate-limit
    // token instead of two. An exact duplicate is never re-queued (as before);
    // if queueing fails on the server, the receipt comes back «uploaded» and
    // its stable detail URL offers an explicit queue action.
    mutationFn: () => uploadReceipt(files, { process: true }),
    onSuccess: (result) => {
      queryClient.setQueryData(["receipts", result.receipt.id], result.receipt);
      void queryClient.invalidateQueries({ queryKey: ["receipts", "recent"] });
      void queryClient.invalidateQueries({ queryKey: myUnsavedReceiptsQueryKey });
      if (result.exactDuplicate) setDuplicate(result.receipt);
      else navigate(`/receipts/${result.receipt.id}`);
    }
  });

  return (
    <Page title={t("receiptCapture.pageTitle")} back={back} end={<HelpLink topic="capture" />}>
      {/* An interrupted batch is decided first: a new batch would replace its stored photos. */}
      {interrupted && !batch.photos
        ? <InterruptedBatchNotice restored={interrupted} disabled={recognition?.available !== true || upload.isPending} onResume={resumeInterrupted} onDiscard={discardInterrupted} />
        : duplicate
          ? <ReceiptDuplicateNotice receiptId={duplicate.id} onStartOver={() => { setDuplicate(null); setFiles([]); }} />
          : (
            <ReceiptCapture
              files={files}
              mode={mode}
              onModeChange={setMode}
              batch={batch.photos}
              uploading={upload.isPending || batch.running}
              disabled={recognition?.available !== true}
              error={upload.isError ? messageOf(t, upload.error, t("legacyCapture.unknownError")) : null}
              onFilesChange={(next) => { setFiles(next); upload.reset(); batch.reset(); }}
              onSubmit={() => separate || batch.photos ? batch.start(files) : upload.mutate()}
              onRetryPhoto={(index) => batch.retry(files, index)}
              onStartOver={() => { batch.reset(); setFiles([]); setMode("parts"); }}
            />
          )}
      {/* Only a problem is shown; the pipeline's model names are not for the household. */}
      {recognition && !recognition.available && (
        <Footnote tone="danger">{recognition.message ?? t("legacyCapture.serviceUnavailable")}</Footnote>
      )}
      <RecentUploads />
    </Page>
  );
}
