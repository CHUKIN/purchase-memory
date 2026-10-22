import { IonInput, IonItem, IonLabel, IonList, IonNote } from "@ionic/react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronRight, ListChecks } from "lucide-react";
import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useLocation, useNavigate, useParams } from "react-router";
import { getPurchase, getReceipt, processReceipt, reviewReceipt, saveReceipt } from "../../api/client";
import type { PurchaseDetail, ReceiptItemSnapshot, ReceiptSnapshot, ReceiptView } from "../../api/types";
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
import { HistoryLayerScope, onLayerEntry, useHistoryLayer } from "../../design-system/history/historyLayers";
import { HelpLink } from "../help/HelpLink";
import { reviewQueueQueryKey } from "../review-queue/reviewQueueQueries";
import { reviewQueueReturnPath } from "../review-queue/returnToQueue";
import { useBackTarget, useReturnState } from "../../app/backNavigation";
import type { LineApprovalSync } from "../line-review/approvalSync";
import { stepLinePosition } from "../line-review/carouselSteps";
import { firstProblemIndex } from "../line-review/lineChecks";
import { useApprovalSync } from "../line-review/useLineApprovals";
import type { LineViewerPhoto } from "../receipt-photo/LinePhoto";
import type { ReceiptPhoto } from "../receipt-photo/photoBox";
import { CaptureLineReview } from "./CaptureLineReview";
import { isDiscount } from "./discountLines";
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
  const approvalSync = useApprovalSync();
  const [snapshot, setSnapshot] = useState<ReceiptSnapshot | null>(null);
  // «Верно, дальше» marks, one per line of `snapshot`; they become the saved purchase's marks.
  const [checked, setChecked] = useState<boolean[]>([]);
  const [lineIndex, setLineIndex] = useState(0);
  const [warningsConfirmed, setWarningsConfirmed] = useState(false);
  // The line carousel is the entry point; the full editor (store, date,
  // total and the lines as a list) is opened explicitly and can be left again.
  const [fullEditorOpen, setFullEditorOpen] = useState(false);
  // After the last product (or «Сохранить сейчас»): the summary with «Сохранить покупку».
  const [summaryOpen, setSummaryOpen] = useState(false);
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
      setChecked(receiptQuery.data.snapshot.items.map(() => false));
      // A discount with a problem is shown in its product's card.
      setLineIndex(stepLinePosition(receiptQuery.data.snapshot.items, firstProblemIndex(receiptQuery.data.snapshot.items)));
      initializedEditorReceiptId.current = receiptQuery.data.id;
    }
  }, [receiptQuery.data]);
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
      let sent = snapshot;
      let marks = checked;
      if (!warningsConfirmed) {
        const reviewed = await reviewReceipt(receiptId, normalizeSnapshot(snapshot));
        queryClient.setQueryData(["receipts", receiptId], reviewed);
        void queryClient.invalidateQueries({ queryKey: ["receipts", receiptId, "auditTrail"] });
        if (reviewed.snapshot) {
          sent = reviewed.snapshot;
          // If the server changed how many lines there are, the marks cannot be matched to lines any more.
          if (reviewed.snapshot.items.length !== snapshot.items.length) marks = reviewed.snapshot.items.map(() => false);
          setSnapshot(reviewed.snapshot);
          setChecked(marks);
        }
        // The carousel only claims to answer structured issues, so unrelated
        // warnings (an unrecognized store, say) must not block saving there;
        // the full editor shows every warning and asks about them.
        const blocking = fullEditorOpen ? reviewed.warnings.length > 0 : reviewed.issues.length > 0;
        if (blocking) {
          // The first save turns into a confirmation step: what is left is
          // listed, and a second, deliberate tap saves anyway.
          setWarningsConfirmed(true);
          return false;
        }
      }
      const purchase = await saveReceipt(receiptId);
      // Not awaited: the marks follow the purchase in the background.
      void queueDraftApprovals(approvalSync, purchase.id, sent.items, marks);
      return true;
    },
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: ["receipts", "recent"] });
      if (saved) {
        void queryClient.invalidateQueries({ queryKey: reviewQueueQueryKey });
        // Saved from the full editor: its history step is replaced, so «назад» from the list cannot reopen it.
        navigate(afterSavePath, { replace: onLayerEntry() });
      }
    }
  });
  const receipt = receiptQuery.data;
  // The full editor is one «назад» step (header chevron, browser, iPhone swipe) back to the carousel.
  const editorShown = fullEditorOpen && receipt?.status === "ready" && snapshot !== null;
  const editorLayer = useHistoryLayer(editorShown, () => setFullEditorOpen(false));
  // So is the summary: «назад» returns to the product it was opened from (the last one after «Верно, к итогу»).
  const summaryShown = summaryOpen && !fullEditorOpen && receipt?.status === "ready" && snapshot !== null;
  const summaryLayer = useHistoryLayer(summaryShown, () => setSummaryOpen(false));
  // In the full editor and the summary the chevron returns to the products on this screen, so it is named «Назад», not the previous screen.
  const layerBack = { to: `/receipts/${receiptId}`, label: t("ui.back") };
  const photoSource = useAuthorizedImageUrl(receipt?.sourceImageUrl);
  const canSave = useMemo(() => snapshot !== null && snapshot.items.length > 0 && snapshot.items.every((item) => item.name.trim()), [snapshot]);
  const updateSnapshot = (next: ReceiptSnapshot) => { setSnapshot(next); setWarningsConfirmed(false); };
  const updateItem = (index: number, patch: Partial<ReceiptItemSnapshot>) => {
    if (!snapshot) return;
    updateSnapshot({ ...snapshot, items: snapshot.items.map((item, position) => position === index ? { ...item, ...patch } : item) });
  };
  const removeItem = (index: number) => {
    if (!snapshot) return;
    updateSnapshot({ ...snapshot, items: snapshot.items.filter((_, position) => position !== index) });
    setChecked((current) => current.filter((_, position) => position !== index));
    setLineIndex((current) => Math.max(0, Math.min(current > index ? current - 1 : current, snapshot.items.length - 2)));
  };
  const restoreItem = (index: number, item: ReceiptItemSnapshot) => {
    if (!snapshot) return;
    const position = Math.min(Math.max(index, 0), snapshot.items.length);
    updateSnapshot({ ...snapshot, items: [...snapshot.items.slice(0, position), item, ...snapshot.items.slice(position)] });
    setChecked((current) => [...current.slice(0, position), false, ...current.slice(position)]);
    setLineIndex((current) => current >= position ? current + 1 : current);
  };
  const addItem = () => {
    if (!snapshot) return;
    updateSnapshot({ ...snapshot, items: [...snapshot.items, emptyItem()] });
    setChecked((current) => [...current, false]);
  };

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
  const reviewing = receipt.status === "ready" && snapshot !== null;
  const carousel = reviewing && !fullEditorOpen;
  const photo: LineViewerPhoto | null = receipt.sourceImageUrl ? { photo: receiptPhoto(receipt, photoSource.src), failed: photoSource.failed } : null;
  const saveButton = (
    <PrimaryButton disabled={!canSave} loading={save.isPending} onClick={() => save.mutate()}>
      {save.isPending ? t("legacyCapture.saving") : warningsConfirmed ? t("legacyCapture.saveAnyway") : t("legacyCapture.savePurchase")}
    </PrimaryButton>
  );
  // The title says where the receipt is; the «?» explains the rest.
  return (
    <Page
      title={title}
      back={editorShown || summaryShown ? layerBack : back}
      // The carousel needs the height: a compact title, like the approved mock.
      largeTitle={!carousel}
      end={receipt.status === "ready" ? <HelpLink topic="review-save" /> : <HelpLink topic="recognition" />}
      footer={carousel && (summaryOpen
        ? saveButton
        : (
          <div className="pm-footer-links">
            <button type="button" className="pm-footer-link" onClick={() => setFullEditorOpen(true)}>
              {t("receiptReview.storeDateTotal")}
            </button>
            <button type="button" className="pm-footer-link" onClick={() => setSummaryOpen(true)}>
              {t("receiptReview.saveNow")}
              <ChevronRight size={16} aria-hidden="true" />
            </button>
          </div>
        ))}
    >
      {/* A recognized draft shows its photo in the carousel; the thumbnails stay only for a receipt in several photos. */}
      {(!reviewing || receipt.imageParts.length > 1) && (
        <ReceiptProgress receipt={receipt} retrying={retry.isPending} onRetry={receipt.status === "failed" || receipt.status === "uploaded" ? () => retry.mutate() : undefined} />
      )}
      {retry.isError && <Notice tone="danger" role="alert"><p>{messageOf(t, retry.error, t("legacyCapture.unknownError"))}</p></Notice>}
      {receipt.status === "saved" && (
        <IonList inset>
          {receipt.savedPurchaseId && <ItemLink to={`/purchases/${receipt.savedPurchaseId}`} state={returnState}><IonLabel color="primary">{t("receiptProgress.openPurchase")}</IonLabel></ItemLink>}
          <ItemLink to="/purchases" lines="none"><IonLabel color="primary">{t("receiptProgress.openPurchases")}</IonLabel></ItemLink>
        </IonList>
      )}
      {reviewing && (
        carousel
          ? (
            <HistoryLayerScope value={summaryLayer}>
              <CaptureLineReview
                snapshot={snapshot}
                index={lineIndex}
                onIndexChange={setLineIndex}
                onChangeItem={updateItem}
                onRemoveItem={removeItem}
                onRestoreItem={restoreItem}
                checked={checked}
                onCheckedChange={(indexes, value) => setChecked((current) => current.map((mark, position) => indexes.includes(position) ? value : mark))}
                onUseLinesTotal={(receiptTotal) => updateSnapshot({ ...snapshot, receiptTotal })}
                remainingIssues={warningsConfirmed ? receipt.issues : null}
                photo={photo}
                receiptId={receipt.id}
                summary={summaryOpen}
                onShowSummary={() => setSummaryOpen(true)}
                onBackToProducts={() => setSummaryOpen(false)}
              />
            </HistoryLayerScope>
          )
          : (
            <HistoryLayerScope value={editorLayer}>
              <ReviewForm
                snapshot={snapshot}
                setSnapshot={updateSnapshot}
                onRemoveItem={removeItem}
                onAddItem={addItem}
                warnings={receipt.warnings}
                saveButton={saveButton}
                onBackToLines={() => setFullEditorOpen(false)}
                photo={photo}
              />
            </HistoryLayerScope>
          )
      )}
      {save.isError && <Notice tone="danger" role="alert"><p>{messageOf(t, save.error, t("legacyCapture.unknownError"))}</p></Notice>}
    </Page>
  );
}

/**
 * Gives the saved purchase the draft's «Верно, дальше» marks, through the
 * same batched queue as the purchase's own line review. The saved lines have
 * ids of their own, so the marks are moved over only when the purchase has
 * exactly the draft's lines in the same order (same names and sums);
 * otherwise they were only a visual aid on the draft screen.
 */
export async function queueDraftApprovals(sync: LineApprovalSync, purchaseId: string, draftItems: ReceiptItemSnapshot[], marks: boolean[]) {
  if (!marks.some(Boolean)) return;
  let saved: PurchaseDetail;
  try {
    saved = await getPurchase(purchaseId);
  } catch {
    return;
  }
  if (!sameLines(saved.items, draftItems)) return;
  marks.forEach((mark, position) => { if (mark) sync.set(purchaseId, saved.items[position].id, true); });
}

function sameLines(saved: PurchaseDetail["items"], draft: ReceiptItemSnapshot[]): boolean {
  if (saved.length !== draft.length) return false;
  return saved.every((item, position) => {
    const other = draft[position];
    const sameTotal = item.lineTotal === null || other.lineTotal === null
      ? item.lineTotal === other.lineTotal
      : Math.abs(item.lineTotal - other.lineTotal) < 0.005;
    return item.name.trim() === other.name.trim() && sameTotal;
  });
}

// A bounding box describes one image, so a multi-part receipt only ever shows
// the first photo whole. A receipt always has at least one stored part.
// The photo URL is protected, so it arrives here already resolved to a blob URL.
function receiptPhoto(receipt: ReceiptView, photoSrc: string | null): ReceiptPhoto | null {
  if (!receipt.sourceImageUrl || !photoSrc) return null;
  return { url: photoSrc, croppable: receipt.imageParts.length <= 1 };
}

const emptyItem = (): ReceiptItemSnapshot => ({ name: "", quantity: null, unit: null, unitPrice: null, lineTotal: null, kind: "product" });

/** The full draft editor: store, date, currency and total, then every line. */
function ReviewForm({ snapshot, setSnapshot, onRemoveItem, onAddItem, warnings, saveButton, onBackToLines, photo }: {
  snapshot: ReceiptSnapshot;
  setSnapshot: (snapshot: ReceiptSnapshot) => void;
  onRemoveItem: (index: number) => void;
  onAddItem: () => void;
  warnings: string[];
  saveButton: ReactNode;
  onBackToLines: () => void;
  photo: LineViewerPhoto | null;
}) {
  const { t } = useTranslation();
  const [openLine, setOpenLine] = useState<number | null>(null);
  const updateItem = (index: number, patch: Partial<ReceiptItemSnapshot>) => setSnapshot({ ...snapshot, items: snapshot.items.map((item, position) => position === index ? { ...item, ...patch } : item) });
  const removeItem = (index: number) => { onRemoveItem(index); setOpenLine(null); };
  const productCount = snapshot.items.filter((item) => !isDiscount(item)).length;
  const discountCount = snapshot.items.length - productCount;
  const currency = snapshot.currency?.trim().toUpperCase() || null;

  return (
    <section aria-labelledby="review-form-title">
      <h2 id="review-form-title" className="visually-hidden">{t("legacyCapture.reviewTitle")}</h2>
      <IonList inset>
        <IonItem button detail lines="none" onClick={onBackToLines}>
          <ListChecks slot="start" size={20} className="pm-row-icon" aria-hidden="true" />
          <IonLabel color="primary">{t("receiptReview.backToLines")}</IonLabel>
        </IonItem>
      </IonList>
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
        help={<HelpLink topic="review-save" />}
        lines={snapshot.items}
        currency={currency}
        openLine={openLine}
        onOpenLine={setOpenLine}
        onChange={updateItem}
        onRemove={removeItem}
        onAdd={() => { onAddItem(); setOpenLine(snapshot.items.length); }}
        photo={photo}
      />

      {saveButton}
    </section>
  );
}

function normalizeSnapshot(snapshot: ReceiptSnapshot): ReceiptSnapshot { return { ...snapshot, merchantName: snapshot.merchantName?.trim() || null, currency: snapshot.currency?.trim().toUpperCase() || null, items: snapshot.items.map((item) => ({ ...item, name: item.name.trim(), unit: item.unit?.trim() || null })) }; }
function toLocalInput(value: string | null): string { if (!value) return ""; const date = new Date(value); return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16); }
