import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo, useRef, useState } from "react";
import type { TFunction } from "i18next";
import { useTranslation } from "react-i18next";
import { Link, useNavigate, useParams } from "react-router";
import { ApiError, getReceipt, processReceipt, reviewReceipt, saveReceipt, uploadReceipt } from "../../api/client";
import { useAuthorizedImageUrl } from "../../auth/useAuthorizedImageUrl";
import type { ReceiptItemSnapshot, ReceiptSnapshot, ReceiptView, RecognitionStatus } from "../../api/types";
import { discountLinks, isDiscount } from "./discountLines";
import { IssueReview, type IssueReviewPhoto } from "./IssueReview";
import { ReceiptDiscountLine } from "./ReceiptDiscountLine";
import { ReceiptCapture } from "./ReceiptCapture";
import { ReceiptDuplicateNotice } from "./ReceiptDuplicateNotice";
import { HelpLink } from "../help/HelpLink";
import { ReceiptProgress } from "./ReceiptProgress";

export function CapturePage({ recognition }: { recognition: RecognitionStatus | null }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [files, setFiles] = useState<File[]>([]);
  const [duplicate, setDuplicate] = useState<ReceiptView | null>(null);
  const upload = useMutation({
    mutationFn: async () => {
      const result = await uploadReceipt(files);
      if (result.exactDuplicate) return result;
      try {
        return { ...result, receipt: await processReceipt(result.receipt.id) };
      } catch {
        // Upload is already durable. The stable detail URL exposes an explicit
        // queue action even when this best-effort initial request failed.
        return result;
      }
    },
    onSuccess: (result) => {
      queryClient.setQueryData(["receipts", result.receipt.id], result.receipt);
      void queryClient.invalidateQueries({ queryKey: ["receipts", "recent"] });
      if (result.exactDuplicate) setDuplicate(result.receipt);
      else navigate(`/receipts/${result.receipt.id}`);
    }
  });

  return <section className="page capture-page">
    <div className="hero-copy"><p className="eyebrow">{t("receiptCapture.eyebrow")}</p><h1>{t("receiptCapture.title")}</h1><p>{t("receiptCapture.description")}</p><HelpLink topic="capture" /></div>
    {duplicate ? <ReceiptDuplicateNotice receiptId={duplicate.id} onStartOver={() => { setDuplicate(null); setFiles([]); }} /> :
      <div className="capture-card"><ReceiptCapture files={files} uploading={upload.isPending} disabled={recognition?.available !== true} error={upload.isError ? messageOf(t, upload.error, t("legacyCapture.unknownError")) : null} onFilesChange={(next) => { setFiles(next); upload.reset(); }} onSubmit={() => upload.mutate()} />
      {recognition && <p className={`model-note ${recognition.available ? "" : "error-text"}`}>{recognition.available ? `${recognition.mode}: ${recognition.models.join(" → ")}` : recognition.message ?? t("legacyCapture.serviceUnavailable")}</p>}</div>}
  </section>;
}

export function ReceiptDetailPage() {
  const { receiptId = "" } = useParams();
  const { t } = useTranslation();
  const navigate = useNavigate();
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
  // Issue review is the default entry point for a receipt that has issues.
  // Opening the full editor is deliberate and sticky for the rest of the visit.
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
          // resolution check — and then a second, deliberate click must still
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
      if (saved) navigate("/purchases");
    }
  });
  const receipt = receiptQuery.data;
  const photoSource = useAuthorizedImageUrl(receipt?.sourceImageUrl);
  const canSave = useMemo(() => snapshot !== null && snapshot.items.length > 0 && snapshot.items.every((item) => item.name.trim()), [snapshot]);
  const updateSnapshot = (next: ReceiptSnapshot) => { setSnapshot(next); setWarningsConfirmed(false); };

  if (receiptQuery.isPending) return <section className="page capture-page"><div className="processing-card" role="status">{t("receiptProgress.loading")}</div></section>;
  if (receiptQuery.isError || !receipt) return <section className="page capture-page"><div className="notice error-notice" role="alert">{messageOf(t, receiptQuery.error, t("receiptProgress.loadFailed"))}</div></section>;
  const description = receipt.status === "ready"
    ? t("receiptProgress.descriptionReady")
    : receipt.status === "failed"
      ? t("receiptProgress.descriptionFailed")
      : receipt.status === "saved"
        ? t("receiptProgress.descriptionSaved")
        : t("receiptProgress.descriptionActive");
  return <section className="page capture-page">
    <div className="hero-copy"><p className="eyebrow">{t("receiptProgress.eyebrow")}</p><h1>{t("receiptProgress.title")}</h1><p>{description}</p>{receipt.status === "ready" ? <HelpLink topic="review-save" /> : <HelpLink topic="recognition" />}</div>
    <ReceiptProgress receipt={receipt} retrying={retry.isPending} onRetry={receipt.status === "failed" || receipt.status === "uploaded" ? () => retry.mutate() : undefined} />
    {retry.isError && <div className="notice error-notice" role="alert">{messageOf(t, retry.error, t("legacyCapture.unknownError"))}</div>}
    {receipt.status === "saved" && <div className="notice"><div><strong>{t("receiptProgress.saved")}</strong><p>{receipt.savedPurchaseId && <><Link to={`/purchases/${receipt.savedPurchaseId}`}>{t("receiptProgress.openPurchase")}</Link>{" · "}</>}<Link to="/purchases">{t("receiptProgress.openPurchases")}</Link></p></div></div>}
    {receipt.status === "ready" && snapshot && (
      showIssueReview
        ? <IssueReview snapshot={snapshot} onSnapshotChange={updateSnapshot} issues={receipt.issues} onOpenFullEditor={() => setFullEditorRequested(true)} onSave={() => save.mutate()} busy={save.isPending} canSave={Boolean(canSave)} photo={receiptPhoto(receipt, photoSource.src)} receiptId={receipt.id} />
        : <ReviewForm snapshot={snapshot} setSnapshot={updateSnapshot} warnings={receipt.warnings} warningsConfirmed={warningsConfirmed} busy={save.isPending} canSave={Boolean(canSave)} onSave={() => save.mutate()} />
    )}
    {save.isError && <div className="notice error-notice" role="alert">{messageOf(t, save.error, t("legacyCapture.unknownError"))}</div>}
  </section>;
}

// A bounding box describes one image, so a multi-part receipt only ever shows
// the first photo whole. A receipt always has at least one stored part.
// The photo URL is protected, so it arrives here already resolved to a blob URL.
function receiptPhoto(receipt: ReceiptView, photoSrc: string | null): IssueReviewPhoto | null {
  if (!receipt.sourceImageUrl || !photoSrc) return null;
  return { url: photoSrc, croppable: receipt.imageParts.length <= 1 };
}

const emptyItem = (): ReceiptItemSnapshot => ({ name: "", quantity: null, unit: null, unitPrice: null, lineTotal: null, kind: "product" });
function ReviewForm({ snapshot, setSnapshot, warnings, warningsConfirmed, busy, canSave, onSave }: { snapshot: ReceiptSnapshot; setSnapshot: (snapshot: ReceiptSnapshot) => void; warnings: string[]; warningsConfirmed: boolean; busy: boolean; canSave: boolean; onSave: () => void }) {
  const { t } = useTranslation();
  function updateItem(index: number, patch: Partial<ReceiptItemSnapshot>) { setSnapshot({ ...snapshot, items: snapshot.items.map((item, position) => position === index ? { ...item, ...patch } : item) }); }
  function removeItem(index: number) { setSnapshot({ ...snapshot, items: snapshot.items.filter((_, position) => position !== index) }); }
  const links = discountLinks(snapshot.items);
  const targetOf = (index: number) => { const target = links[index].target; return target === null ? null : snapshot.items[target]; };
  const productCount = snapshot.items.filter((item) => !isDiscount(item)).length;
  const discountCount = snapshot.items.length - productCount;
  return <div className="review-panel">
    <div className="section-heading"><div><p className="eyebrow">{t("legacyCapture.draft")}</p><h2>{t("legacyCapture.reviewTitle")}</h2></div><span className="item-count">{t("legacyCapture.positions", { count: productCount })}{discountCount > 0 && ` · ${t("discount.count", { count: discountCount })}`}</span></div>
    <div className="form-grid"><Field label={t("legacyCapture.store")} value={snapshot.merchantName ?? ""} onChange={(value) => setSnapshot({ ...snapshot, merchantName: value || null })} /><Field label={t("legacyCapture.dateTime")} type="datetime-local" value={toLocalInput(snapshot.purchasedAt)} onChange={(value) => setSnapshot({ ...snapshot, purchasedAt: value ? new Date(value).toISOString() : null })} /><Field label={t("legacyCapture.currency")} value={snapshot.currency ?? ""} onChange={(value) => setSnapshot({ ...snapshot, currency: value.toUpperCase() || null })} /><NumberField label={t("legacyCapture.total")} value={snapshot.receiptTotal} onChange={(value) => setSnapshot({ ...snapshot, receiptTotal: value })} /></div>
    {warnings.length > 0 && <div className="review-warnings" role="status"><strong>{t("legacyCapture.needsReview")}</strong><ul>{warnings.map((warning) => <li key={warning}>{warning}</li>)}</ul></div>}
    <div className="items-list">{snapshot.items.map((item, index) => isDiscount(item) ? <ReceiptDiscountLine key={index} name={item.name} lineTotal={item.lineTotal} currency={snapshot.currency} productName={targetOf(index)?.name || null} productLineTotal={targetOf(index)?.lineTotal ?? null} appliesToReceipt={links[index].appliesToReceipt} onChange={(change) => updateItem(index, change)} onDelete={() => removeItem(index)} /> : <article className="item-editor" key={index}><div className="item-editor-header"><span>{t("legacyCapture.position", { count: index + 1 })}</span><button className="icon-button" aria-label={t("legacyCapture.deletePosition", { count: index + 1 })} onClick={() => removeItem(index)}>×</button></div><Field label={t("legacyCapture.name")} value={item.name} onChange={(value) => updateItem(index, { name: value })} /><div className="item-numbers"><NumberField label={t("legacyCapture.quantity")} value={item.quantity} step="0.001" onChange={(value) => updateItem(index, { quantity: value })} /><NumberField label={t("legacyCapture.price")} value={item.unitPrice} onChange={(value) => updateItem(index, { unitPrice: value })} /><NumberField label={t("legacyCapture.amount")} value={item.lineTotal} onChange={(value) => updateItem(index, { lineTotal: value })} /></div></article>)}</div>
    <button className="secondary-button add-item-button" onClick={() => setSnapshot({ ...snapshot, items: [...snapshot.items, emptyItem()] })}>＋ {t("legacyCapture.addPosition")}</button><button className="primary-button sticky-save" disabled={!canSave || busy} onClick={onSave}>{busy ? t("legacyCapture.saving") : warningsConfirmed ? t("legacyCapture.saveAnyway") : t("legacyCapture.savePurchase")}</button>
  </div>;
}
function Field({ label, value, onChange, type = "text" }: { label: string; value: string; onChange: (value: string) => void; type?: string }) { return <label className="field"><span>{label}</span><input type={type} value={value} onChange={(event) => onChange(event.target.value)} /></label>; }
function NumberField({ label, value, onChange, step = "0.01" }: { label: string; value: number | null; onChange: (value: number | null) => void; step?: string }) { return <label className="field"><span>{label}</span><input inputMode="decimal" type="number" min="0" step={step} value={value ?? ""} onChange={(event) => onChange(event.target.value === "" ? null : Number(event.target.value))} /></label>; }
function normalizeSnapshot(snapshot: ReceiptSnapshot): ReceiptSnapshot { return { ...snapshot, merchantName: snapshot.merchantName?.trim() || null, currency: snapshot.currency?.trim().toUpperCase() || null, items: snapshot.items.map((item) => ({ ...item, name: item.name.trim(), unit: item.unit?.trim() || null })) }; }
function toLocalInput(value: string | null): string { if (!value) return ""; const date = new Date(value); return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16); }
function messageOf(t: TFunction, error: unknown, fallback: string): string {
  if (error instanceof ApiError && error.status === 429) {
    return t("auth.rateLimited", { minutes: Math.max(1, Math.ceil((error.retryAfterSeconds ?? 60) / 60)) });
  }
  return error instanceof Error ? error.message : fallback;
}
