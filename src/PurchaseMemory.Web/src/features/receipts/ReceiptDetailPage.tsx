import { IonInput, IonItem, IonLabel, IonList, IonNote } from "@ionic/react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useLocation, useNavigate, useParams } from "react-router";
import { getReceipt, processReceipt, reviewReceipt, saveReceipt } from "../../api/client";
import type { ReceiptItemSnapshot, ReceiptSnapshot, ReceiptView } from "../../api/types";
import { useAuthorizedImageUrl } from "../../auth/useAuthorizedImageUrl";
import {
  DateField,
  DecimalInput,
  ItemLink,
  LoadingList,
  Notice,
  Page,
  PrimaryButton
} from "../../design-system/components";
import { HelpLink } from "../help/HelpLink";
import { reviewQueueQueryKey } from "../review-queue/reviewQueueQueries";
import { reviewQueueReturnPath } from "../review-queue/returnToQueue";
import { useBackTarget, useReturnState } from "../../app/backNavigation";
import { isDiscount } from "./discountLines";
import { IssueReview, type IssueReviewPhoto } from "./IssueReview";
import { LinesEditor } from "./LinesEditor";
import { ReceiptProgress } from "./ReceiptProgress";
import { messageOf } from "./receiptErrors";

export function ReceiptDetailPage() {
  const { receiptId = "" } = useParams();
  const { t } = useTranslation();
  const navigate = useNavigate();
  // Opened from «Нужно проверить»: saving returns there (filters kept) instead of the history.
  const queuePath = reviewQueueReturnPath(useLocation().state);
  const afterSavePath = queuePath ?? "/purchases";
  const back = useBackTarget({ to: "/", labelKey: "common.home" });
  const returnState = useReturnState();
  const queryClient = useQueryClient();
  const [snapshot, setSnapshot] = useState<ReceiptSnapshot | null>(null);
  const [warningsConfirmed, setWarningsConfirmed] = useState(false);
  // Issue-based review is the entry point whenever the receipt has issues; the
  // full editor is reached only by an explicit action and then stays open.
  const [fullEditorRequested, setFullEditorRequested] = useState(false);
  const initializedEditorReceiptId = useRef<string | null>(null);
  const receiptQuery = useQuery({
    queryKey: ["receipts", receiptId],
    queryFn: () => getReceipt(receiptId),
    enabled: receiptId.length > 0,
    staleTime: 0,
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      return status && ["uploaded", "queued", "processing"].includes(status) ? 1_200 : false;
    }
  });
  useEffect(() => {
    if (receiptQuery.data?.snapshot && initializedEditorReceiptId.current !== receiptQuery.data.id) {
      setSnapshot(receiptQuery.data.snapshot);
      initializedEditorReceiptId.current = receiptQuery.data.id;
    }
  }, [receiptQuery.data]);
  const showIssueReview = !fullEditorRequested && (receiptQuery.data?.issues.length ?? 0) > 0;
  const retry = useMutation({
    mutationFn: () => processReceipt(receiptId),
    onSuccess: (receipt) => {
      queryClient.setQueryData(["receipts", receiptId], receipt);
      void queryClient.invalidateQueries({ queryKey: ["receipts", "recent"] });
    }
  });
  const save = useMutation({
    mutationFn: async () => {
      if (!snapshot) throw new Error(t("legacyCapture.unknownError"));
      if (!warningsConfirmed) {
        const reviewed = await reviewReceipt(receiptId, normalizeSnapshot(snapshot));
        queryClient.setQueryData(["receipts", receiptId], reviewed);
        void queryClient.invalidateQueries({ queryKey: ["receipts", receiptId, "auditTrail"] });
        setSnapshot(reviewed.snapshot);
        // Issue review only claims to answer structured issues, so unrelated
        // warnings (an unrecognized store, say) must not block saving there.
        const blocking = showIssueReview ? reviewed.issues.length > 0 : reviewed.warnings.length > 0;
        if (blocking) {
          // The first save turns into a confirmation step in both modes. In
          // issue review the cards normally gate the button long before this,
          // so it only fires when the server disagrees with the client's own
          // resolution check — and then a second, deliberate tap must still
          // get through, or that disagreement would be an inescapable dead end.
          setWarningsConfirmed(true);
          return false;
        }
      }
      await saveReceipt(receiptId);
      return true;
    },
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: ["receipts", "recent"] });
      if (saved) {
        void queryClient.invalidateQueries({ queryKey: reviewQueueQueryKey });
        navigate(afterSavePath);
      }
    }
  });
  const receipt = receiptQuery.data;
  const photoSource = useAuthorizedImageUrl(receipt?.sourceImageUrl);
  const canSave = useMemo(() => snapshot !== null && snapshot.items.length > 0 && snapshot.items.every((item) => item.name.trim()), [snapshot]);
  const updateSnapshot = (next: ReceiptSnapshot) => { setSnapshot(next); setWarningsConfirmed(false); };

  if (receiptQuery.isPending) {
    return <Page title={t("receiptJobs.receipt")} back={back}><LoadingList label={t("receiptProgress.loading")} rows={2} /></Page>;
  }
  if (receiptQuery.isError || !receipt) {
    return (
      <Page title={t("receiptJobs.receipt")} back={back}>
        <Notice tone="danger" role="alert"><p>{messageOf(t, receiptQuery.error, t("receiptProgress.loadFailed"))}</p></Notice>
      </Page>
    );
  }
  // Only a saved receipt is «сохранён»: a recognized draft is not in the history yet.
  const title = receipt.status === "ready"
    ? t("receiptProgress.titleReady")
    : receipt.status === "failed"
      ? t("receiptProgress.titleFailed")
      : receipt.status === "saved"
        ? t("receiptProgress.titleSaved")
        : t("receiptProgress.titleActive");
  const description = receipt.status === "ready"
    ? t("receiptProgress.descriptionReady")
    : receipt.status === "failed"
      ? t("receiptProgress.descriptionFailed")
      : receipt.status === "saved"
        ? t("receiptProgress.descriptionSaved")
        : t("receiptProgress.descriptionActive");

  return (
    <Page title={title} back={back}>
      <p className="pm-home-intro">{description} {receipt.status === "ready" ? <HelpLink topic="review-save" /> : <HelpLink topic="recognition" />}</p>
      <ReceiptProgress receipt={receipt} retrying={retry.isPending} onRetry={receipt.status === "failed" || receipt.status === "uploaded" ? () => retry.mutate() : undefined} />
      {retry.isError && <Notice tone="danger" role="alert"><p>{messageOf(t, retry.error, t("legacyCapture.unknownError"))}</p></Notice>}
      {receipt.status === "saved" && (
        <>
          <Notice tone="success"><p><strong>{t("receiptProgress.saved")}</strong></p></Notice>
          <IonList inset>
            {receipt.savedPurchaseId && <ItemLink to={`/purchases/${receipt.savedPurchaseId}`} state={returnState}><IonLabel color="primary">{t("receiptProgress.openPurchase")}</IonLabel></ItemLink>}
            <ItemLink to="/purchases" lines="none"><IonLabel color="primary">{t("receiptProgress.openPurchases")}</IonLabel></ItemLink>
          </IonList>
        </>
      )}
      {receipt.status === "ready" && snapshot && (
        showIssueReview
          ? <IssueReview snapshot={snapshot} onSnapshotChange={updateSnapshot} issues={receipt.issues} onOpenFullEditor={() => setFullEditorRequested(true)} onSave={() => save.mutate()} busy={save.isPending} canSave={Boolean(canSave)} photo={receiptPhoto(receipt, photoSource.src)} receiptId={receipt.id} />
          : <ReviewForm snapshot={snapshot} setSnapshot={updateSnapshot} warnings={receipt.warnings} warningsConfirmed={warningsConfirmed} busy={save.isPending} canSave={Boolean(canSave)} onSave={() => save.mutate()} />
      )}
      {save.isError && <Notice tone="danger" role="alert"><p>{messageOf(t, save.error, t("legacyCapture.unknownError"))}</p></Notice>}
    </Page>
  );
}

// A bounding box describes one image, so a multi-part receipt only ever shows
// the first photo whole. A receipt always has at least one stored part.
// The photo URL is protected, so it arrives here already resolved to a blob URL.
function receiptPhoto(receipt: ReceiptView, photoSrc: string | null): IssueReviewPhoto | null {
  if (!receipt.sourceImageUrl || !photoSrc) return null;
  return { url: photoSrc, croppable: receipt.imageParts.length <= 1 };
}

const emptyItem = (): ReceiptItemSnapshot => ({ name: "", quantity: null, unit: null, unitPrice: null, lineTotal: null, kind: "product" });

/** The full draft editor: store, date, currency and total, then every line. */
function ReviewForm({ snapshot, setSnapshot, warnings, warningsConfirmed, busy, canSave, onSave }: { snapshot: ReceiptSnapshot; setSnapshot: (snapshot: ReceiptSnapshot) => void; warnings: string[]; warningsConfirmed: boolean; busy: boolean; canSave: boolean; onSave: () => void }) {
  const { t } = useTranslation();
  const [openLine, setOpenLine] = useState<number | null>(null);
  const updateItem = (index: number, patch: Partial<ReceiptItemSnapshot>) => setSnapshot({ ...snapshot, items: snapshot.items.map((item, position) => position === index ? { ...item, ...patch } : item) });
  const removeItem = (index: number) => { setSnapshot({ ...snapshot, items: snapshot.items.filter((_, position) => position !== index) }); setOpenLine(null); };
  const productCount = snapshot.items.filter((item) => !isDiscount(item)).length;
  const discountCount = snapshot.items.length - productCount;
  const currency = snapshot.currency?.trim().toUpperCase() || null;

  return (
    <section aria-labelledby="review-form-title">
      <h2 id="review-form-title" className="visually-hidden">{t("legacyCapture.reviewTitle")}</h2>
      <IonList inset>
        <IonItem>
          <IonInput label={t("legacyCapture.store")} labelPlacement="fixed" value={snapshot.merchantName ?? ""} onIonInput={(event) => setSnapshot({ ...snapshot, merchantName: String(event.detail.value ?? "") || null })} />
        </IonItem>
        <DateField
          label={t("legacyCapture.dateTime")}
          withTime
          formValue
          emptyText={t("purchaseEdit.unknownDate")}
          value={toLocalInput(snapshot.purchasedAt)}
          onChange={(value) => setSnapshot({ ...snapshot, purchasedAt: value ? new Date(value).toISOString() : null })}
        />
        <IonItem>
          <IonInput label={t("legacyCapture.currency")} labelPlacement="fixed" maxlength={3} value={snapshot.currency ?? ""} onIonInput={(event) => setSnapshot({ ...snapshot, currency: String(event.detail.value ?? "").toUpperCase() || null })} />
        </IonItem>
        <IonItem lines="none">
          <DecimalInput label={t("legacyCapture.total")} value={snapshot.receiptTotal} onChange={(receiptTotal) => setSnapshot({ ...snapshot, receiptTotal })} />
          {currency && <IonNote slot="end">{currency}</IonNote>}
        </IonItem>
      </IonList>

      {warnings.length > 0 && (
        <Notice tone="warning" role="status">
          <p><strong>{t("legacyCapture.needsReview")}</strong></p>
          <ul className="pm-notice-list">{warnings.map((warning) => <li key={warning}>{warning}</li>)}</ul>
        </Notice>
      )}

      <LinesEditor
        title={`${t("legacyCapture.positions", { count: productCount })}${discountCount > 0 ? ` · ${t("discount.count", { count: discountCount })}` : ""}`}
        lines={snapshot.items}
        currency={currency}
        openLine={openLine}
        onOpenLine={setOpenLine}
        onChange={updateItem}
        onRemove={removeItem}
        onAdd={() => { setSnapshot({ ...snapshot, items: [...snapshot.items, emptyItem()] }); setOpenLine(snapshot.items.length); }}
      />

      <PrimaryButton disabled={!canSave} loading={busy} onClick={onSave}>
        {busy ? t("legacyCapture.saving") : warningsConfirmed ? t("legacyCapture.saveAnyway") : t("legacyCapture.savePurchase")}
      </PrimaryButton>
    </section>
  );
}

function normalizeSnapshot(snapshot: ReceiptSnapshot): ReceiptSnapshot { return { ...snapshot, merchantName: snapshot.merchantName?.trim() || null, currency: snapshot.currency?.trim().toUpperCase() || null, items: snapshot.items.map((item) => ({ ...item, name: item.name.trim(), unit: item.unit?.trim() || null })) }; }
function toLocalInput(value: string | null): string { if (!value) return ""; const date = new Date(value); return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16); }
