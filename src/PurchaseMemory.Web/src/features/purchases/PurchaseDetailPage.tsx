import { IonButton, IonInput, IonItem, IonLabel, IonList, IonNote } from "@ionic/react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, CalendarClock, CircleCheck, ListChecks, Pencil, ReceiptText } from "lucide-react";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useLocation, useNavigate, useParams, useSearchParams } from "react-router";
import { ApiError, getPurchase, updatePurchase } from "../../api/client";
import type {
  BoundingBox,
  ProductLinkChoice,
  PurchaseDetail,
  ReceiptLineKind,
  UpdatePurchaseRequest
} from "../../api/types";
import { useToast } from "../../app/toasts";
import {
  ActionButton,
  type BackTarget,
  ButtonLink,
  DateField,
  DecimalInput,
  Footnote,
  ItemLink,
  LoadingList,
  Notice,
  Page,
  SectionHeader
} from "../../design-system/components";
import { issueCopy, issueKey, moneyComparisonTolerance } from "../issues/issueText";
import { isDiscount, discountLinks } from "../receipts/discountLines";
import { LinesEditor, type LineFocus } from "../receipts/LinesEditor";
import { ReceiptDiscountLine } from "../receipts/ReceiptDiscountLine";
import { reviewQueueQueryKey } from "../review-queue/reviewQueueQueries";
import { reviewQueueReturnPath } from "../review-queue/returnToQueue";
import { useBackTarget, useReturnState } from "../../app/backNavigation";
import { PurchaseLineImpressions, usePurchaseImpressions } from "../impressions/ImpressionSections";
import { impressionsQueryKey } from "../impressions/impressionText";
import { HelpLink } from "../help/HelpLink";
import { formatShortDate } from "./dates";
import { formatMoney } from "./money";
import { ProductLinkNote, ProductLinkRows, productStatusText } from "./ProductLinkRows";
import { LineReview, type ReviewLinePatch, type ReviewStart } from "../line-review/LineReview";
import { useApprovalSync, useLineApprovals } from "../line-review/useLineApprovals";
import { usePurchaseReceiptPhoto } from "./usePurchaseReceiptPhoto";

export const purchaseQueryKey = (purchaseId: string) => ["purchases", "detail", purchaseId] as const;

/**
 * Where the review queue sends a person: `focus=total`, `focus=price-3` or
 * `focus=product-3` (zero-based line). Kept in one place so links and the
 * editor cannot drift apart.
 */
export function purchaseEditPath(purchaseId: string, focus?: string) {
  const parameters = new URLSearchParams({ edit: "1" });
  if (focus) parameters.set("focus", focus);
  return `/purchases/${encodeURIComponent(purchaseId)}?${parameters}`;
}

export function PurchaseDetailPage() {
  const { purchaseId = "" } = useParams();
  const [searchParameters, setSearchParameters] = useSearchParams();
  const { t } = useTranslation();
  const showToast = useToast();
  const query = useQuery({
    queryKey: purchaseQueryKey(purchaseId),
    queryFn: () => getPurchase(purchaseId),
    enabled: purchaseId.length > 0,
    retry: (count, error) => !(error instanceof ApiError && error.status === 404) && count < 2
  });
  const editing = searchParameters.get("edit") === "1";
  const focus = searchParameters.get("focus");

  // Opened from «Нужно проверить»: saving returns there (filters kept), like a
  // receipt does. The router state is carried across the edit/view switches so
  // «Открыть покупку» → «Исправить» → «Сохранить» still goes back to the queue.
  const location = useLocation();
  const navigate = useNavigate();
  const queueReturnPath = reviewQueueReturnPath(location.state);
  const keepState = { state: location.state as unknown };
  const startEditing = () => setSearchParameters({ edit: "1" }, keepState);
  const stopEditing = (saved: boolean) => {
    if (saved) showToast({ id: `purchase-saved-${Date.now()}`, message: t("purchaseEdit.saved"), tone: "success" });
    if (saved && queueReturnPath) { navigate(queueReturnPath); return; }
    setSearchParameters({}, keepState);
  };
  const back = useBackTarget({ to: "/purchases", labelKey: "common.purchases" });

  if (query.isPending) {
    return <Page title={t("purchaseEdit.eyebrow")} back={back}><LoadingList label={t("purchaseEdit.loading")} /></Page>;
  }

  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404;
    return (
      <Page title={t("purchaseEdit.eyebrow")} back={back}>
        <Notice tone="danger" role="alert">
          <p>{notFound ? t("purchaseEdit.notFound") : t("purchaseEdit.loadError")}</p>
          {!notFound && <IonButton fill="outline" size="small" onClick={() => void query.refetch()}>{t("purchaseEdit.retry")}</IonButton>}
        </Notice>
        <div className="ion-padding-horizontal"><ButtonLink to="/purchases" fill="clear">{t("purchaseEdit.backToPurchases")}</ButtonLink></div>
      </Page>
    );
  }

  const purchase = query.data;
  return editing
    ? <PurchaseEditor key={purchase.updatedAt} purchase={purchase} focus={focus} onDone={stopEditing} onReload={() => void query.refetch()} />
    : <PurchaseSummary purchase={purchase} back={back} onEdit={startEditing} />;
}

function PurchaseSummary({ purchase, back, onEdit }: { purchase: PurchaseDetail; back: BackTarget; onEdit: () => void }) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage === "en" ? "en-GB" : "ru-RU";
  const money = (value: number | null) => value === null ? "—" : formatMoney(value, purchase.currency, locale);
  const productCount = purchase.items.filter((item) => !isDiscount(item)).length;
  const links = discountLinks(purchase.items);
  // Optional context: if it fails to load the purchase is still fully usable.
  const impressions = usePurchaseImpressions(purchase.id);
  const receiptPhoto = usePurchaseReceiptPhoto(purchase.sourceReceiptId);
  const photoUrl = receiptPhoto.url;
  const photo = receiptPhoto.viewer;
  const approvals = useLineApprovals(purchase, purchaseQueryKey(purchase.id));
  // Tapping a line opens the swipe review at it; the header button at the
  // first line nobody has checked.
  const [review, setReview] = useState<ReviewStart | null>(null);
  const openReview = (index: number | null) => setReview((current) => ({ index, nonce: (current?.nonce ?? 0) + 1 }));
  const saveLine = useSaveLine(purchase, approvals);
  const returnState = useReturnState();
  const total = purchase.items.length;

  return (
    <Page
      title={purchase.merchantName ?? t("purchaseEdit.unknownStore")}
      back={back}
      end={(
        <>
          <HelpLink topic="purchase-edit" />
          <IonButton className="pm-icon-button" onClick={onEdit} aria-label={t("purchaseEdit.edit")}>
            <Pencil size={22} aria-hidden="true" />
          </IonButton>
        </>
      )}
    >
      <IonList inset>
        <IonItem>
          <CalendarClock slot="start" size={22} className="pm-row-icon pm-muted-icon" aria-hidden="true" />
          <IonLabel>
            <span className="visually-hidden">{t("purchaseEdit.dateTime")}: </span>
            {purchase.purchasedAt ? formatShortDate(purchase.purchasedAt, locale, { time: true }) : t("purchaseEdit.unknownDate")}
          </IonLabel>
          <IonNote slot="end" className="pm-amount pm-total">
            <span className="visually-hidden">{t("purchaseEdit.total")} </span>{money(purchase.receiptTotal)}
          </IonNote>
        </IonItem>
        {purchase.sourceReceiptId && (
          <ItemLink to={`/receipts/${purchase.sourceReceiptId}`} state={returnState} lines="none">
            <ReceiptText slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
            <IonLabel color="primary">{t("purchaseEdit.openReceipt")}</IonLabel>
          </ItemLink>
        )}
      </IonList>

      {/* Only problems are shown; a clean purchase needs no «all fine» sentence. */}
      {purchase.issues.length > 0 && (
        <section aria-label={t("purchaseEdit.issuesTitle")}>
          <IonList inset>
            {purchase.issues.map((issue, index) => (
              <IonItem key={issueKey(issue)} lines={index === purchase.issues.length - 1 ? "none" : undefined}>
                <AlertTriangle slot="start" size={20} className="pm-row-icon pm-row-icon-warning" aria-hidden="true" />
                <IonLabel className="pm-wrap pm-warn-text">{issueCopy(t, issue).title}</IonLabel>
              </IonItem>
            ))}
          </IonList>
        </section>
      )}

      <section aria-labelledby="purchase-lines">
        <SectionHeader
          id="purchase-lines"
          action={(
            <span className="pm-header-actions">
              <HelpLink topic="receipt-lines-photo" />
              <IonButton
                fill="clear"
                className="pm-review-start"
                onClick={() => openReview(null)}
                aria-label={t("lineReview.start", { approved: approvals.approvedCount, total })}
              >
                <ListChecks size={20} aria-hidden="true" />
                <span aria-hidden="true">{approvals.approvedCount}/{total}</span>
              </IonButton>
            </span>
          )}
        >
          {t("purchaseEdit.linesCount", { count: productCount })}
        </SectionHeader>
        <IonList inset className="pm-after-header">
          {purchase.items.map((item, index) => {
            if (isDiscount(item)) {
              const target = links[index].target;
              return (
                <IonItem key={item.position} button detail={false} onClick={() => openReview(index)}>
                  <ReceiptDiscountLine
                    name={item.name}
                    lineTotal={item.lineTotal}
                    currency={purchase.currency}
                    productName={target === null ? null : purchase.items[target].name}
                    productLineTotal={target === null ? null : purchase.items[target].lineTotal}
                    appliesToReceipt={links[index].appliesToReceipt}
                  />
                  <ApprovedMark approved={approvals.isApproved(item)} />
                  <span className="visually-hidden">{t("lineReview.openLine")}</span>
                </IonItem>
              );
            }
            return (
              <IonItem key={item.position}>
                <IonLabel className="pm-wrap">
                  <ReviewLineButton onOpen={() => openReview(index)} approved={approvals.isApproved(item)} name={item.name}>
                    <ProductLinkNote
                      text={item.productName
                        ? productStatusText(t, { ...item, productLink: "existing" })
                        : t("purchaseEdit.productUnlinked")}
                      lineName={item.name}
                      productName={item.productName}
                      flagged={Boolean(item.productName && item.productNeedsReview)}
                      unlinked={!item.productName}
                    />
                    {item.unitPrice === null && <span className="pm-line-detail" data-warn="true">{t("purchaseEdit.missingPrice")}</span>}
                  </ReviewLineButton>
                  {impressions.data && <PurchaseLineImpressions item={item} impressions={impressions.data} locale={locale} />}
                </IonLabel>
                <IonNote slot="end" className="pm-amount">{money(item.lineTotal)}</IonNote>
              </IonItem>
            );
          })}
        </IonList>
      </section>

      <LineReview
        start={review}
        onClose={() => setReview(null)}
        lines={purchase.items}
        isApproved={approvals.isApproved}
        setApproved={approvals.setApproved}
        onSaveLine={saveLine}
        photo={photo}
        currency={purchase.currency}
      />

      {/* No photo, no section: the purchase simply was saved without one. */}
      {photoUrl && (
        <section aria-label={t("purchaseEdit.photoAlt")} className="pm-photo-section">
          {photo?.photo && <div className="pm-photo-card"><img className="pm-photo" src={photo.photo.url} alt={t("purchaseEdit.photoAlt")} /></div>}
          {photo?.failed && <Footnote>{t("purchaseEdit.photoFailed")}</Footnote>}
        </section>
      )}
    </Page>
  );
}

/**
 * A purchase line's name and status as one button that opens the swipe
 * review at this line; the line's other controls (like «Оценить») stay
 * outside it, so no control is nested in another. A checked line shows ✓.
 */
function ReviewLineButton({ onOpen, approved, name, children }: {
  onOpen: () => void;
  approved: boolean;
  name: string;
  children: ReactNode;
}) {
  const { t } = useTranslation();
  return (
    <button type="button" className="pm-line-locate" onClick={onOpen}>
      <span className="pm-line-name">
        {name}
        <ApprovedMark approved={approved} />
      </span>
      {children}
      <span className="visually-hidden">{t("lineReview.openLine")}</span>
    </button>
  );
}

/** The small ✓ of a line someone has checked in the swipe review. */
function ApprovedMark({ approved }: { approved: boolean }) {
  const { t } = useTranslation();
  if (!approved) return null;
  return (
    <span className="pm-line-checked">
      <CircleCheck size={15} aria-hidden="true" />
      <span className="visually-hidden">{t("lineReview.checkedSuffix")}</span>
    </span>
  );
}

/**
 * The swipe review's «correct this line»: the purchase is saved with just
 * that line changed (the same full snapshot the editor sends), and the
 * corrected line counts as checked. Unsent marks go first, because a
 * correction replaces the line rows.
 */
function useSaveLine(purchase: PurchaseDetail, approvals: ReturnType<typeof useLineApprovals>) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  return async (index: number, patch: ReviewLinePatch) => {
    await approvals.flush();
    const state = toEditState(purchase);
    state.items = state.items.map((item, position) => position === index ? { ...item, ...patch } : item);
    let updated: PurchaseDetail;
    try {
      updated = await updatePurchase(purchase.id, toRequest(purchase, state));
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) {
        void queryClient.invalidateQueries({ queryKey: purchaseQueryKey(purchase.id) });
        throw new Error(t("lineReview.conflict"));
      }
      throw error;
    }
    queryClient.setQueryData(purchaseQueryKey(purchase.id), updated);
    const line = updated.items[index];
    if (line) approvals.setApproved(line.id, true);
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: reviewQueueQueryKey }),
      queryClient.invalidateQueries({ queryKey: ["purchases", "feed"] }),
      queryClient.invalidateQueries({ queryKey: ["products"] }),
      // Impressions follow the replaced line rows to their new ids.
      queryClient.invalidateQueries({ queryKey: impressionsQueryKey }),
      purchase.sourceReceiptId ? queryClient.invalidateQueries({ queryKey: ["receipts", purchase.sourceReceiptId] }) : Promise.resolve()
    ]);
  };
}

interface EditLine {
  key: string;
  /** The saved line this row continues (keeps its «checked» mark); null for a new row. */
  id: string | null;
  name: string;
  quantity: number | null;
  unit: string | null;
  unitPrice: number | null;
  lineTotal: number | null;
  kind: ReceiptLineKind;
  productLink: ProductLinkChoice;
  productId: string | null;
  productName: string | null;
  productNeedsReview: boolean;
  boundingBox: BoundingBox | null;
}

interface EditState {
  merchantName: string;
  purchasedAt: string;
  currency: string;
  receiptTotal: number | null;
  items: EditLine[];
}

let lineKeySequence = 0;
const nextKey = () => `line-${++lineKeySequence}`;

function toEditState(purchase: PurchaseDetail): EditState {
  return {
    merchantName: purchase.merchantName ?? "",
    purchasedAt: toLocalInput(purchase.purchasedAt),
    currency: purchase.currency ?? "",
    receiptTotal: purchase.receiptTotal,
    items: purchase.items.map((item) => ({
      key: nextKey(),
      id: item.id,
      name: item.name,
      quantity: item.quantity,
      unit: item.unit,
      unitPrice: item.unitPrice,
      lineTotal: item.lineTotal,
      kind: item.kind,
      productLink: item.productId ? "existing" : "auto",
      productId: item.productId,
      productName: item.productName,
      productNeedsReview: Boolean(item.productNeedsReview),
      boundingBox: item.boundingBox
    }))
  };
}

function toRequest(purchase: PurchaseDetail, state: EditState): UpdatePurchaseRequest {
  return {
    expectedUpdatedAt: purchase.updatedAt,
    merchantName: state.merchantName.trim() || null,
    purchasedAt: state.purchasedAt ? new Date(state.purchasedAt).toISOString() : null,
    currency: state.currency.trim().toUpperCase() || null,
    receiptTotal: state.receiptTotal,
    items: state.items.map((item) => ({
      name: item.name.trim(),
      quantity: item.quantity,
      unit: item.unit?.trim() || null,
      unitPrice: item.unitPrice,
      lineTotal: item.lineTotal,
      kind: item.kind,
      productLink: item.kind === "discount" ? "auto" : item.productLink,
      productId: item.kind === "discount" || item.productLink !== "existing" ? null : item.productId,
      boundingBox: item.boundingBox,
      id: item.id
    }))
  };
}

function PurchaseEditor({ purchase, focus, onDone, onReload }: {
  purchase: PurchaseDetail;
  focus: string | null;
  onDone: (saved: boolean) => void;
  onReload: () => void;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [state, setState] = useState(() => toEditState(purchase));
  const [validation, setValidation] = useState<string | null>(null);
  const target = parseFocus(focus);
  const [openLine, setOpenLine] = useState<number | null>(() => target && target.kind !== "total" ? target.line : null);
  const [lineFocus] = useState<LineFocus | null>(() => target?.kind === "price" ? "price" : null);
  const [pickerOpenFor, setPickerOpenFor] = useState<number | null>(() => target?.kind === "product" ? target.line : null);
  const totalRef = useRef<HTMLIonInputElement>(null);
  const merchantRef = useRef<HTMLIonInputElement>(null);
  const receiptPhoto = usePurchaseReceiptPhoto(purchase.sourceReceiptId);
  const approvalSync = useApprovalSync();

  // A review-queue link lands on the flagged field; otherwise the store name.
  useEffect(() => {
    if (target && target.kind !== "total") return;
    const element = target?.kind === "total" ? totalRef.current : merchantRef.current;
    const timer = window.setTimeout(() => void element?.setFocus?.(), 300);
    return () => window.clearTimeout(timer);
    // Only on arrival.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const save = useMutation({
    mutationFn: async () => {
      // A correction replaces the line rows: unsent «checked» marks go first,
      // so the server can carry them over to the corrected lines.
      await approvalSync.flush();
      return updatePurchase(purchase.id, toRequest(purchase, state));
    },
    onSuccess: async (updated) => {
      queryClient.setQueryData(purchaseQueryKey(purchase.id), updated);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: reviewQueueQueryKey }),
        queryClient.invalidateQueries({ queryKey: ["purchases", "feed"] }),
        queryClient.invalidateQueries({ queryKey: ["products"] }),
        // Impressions follow the replaced line rows to their new ids.
        queryClient.invalidateQueries({ queryKey: impressionsQueryKey }),
        purchase.sourceReceiptId ? queryClient.invalidateQueries({ queryKey: ["receipts", purchase.sourceReceiptId] }) : Promise.resolve()
      ]);
      onDone(true);
    }
  });

  const update = (patch: Partial<EditState>) => { setState((current) => ({ ...current, ...patch })); setValidation(null); };
  const updateItem = (index: number, patch: Partial<EditLine>) =>
    setState((current) => ({ ...current, items: current.items.map((item, position) => position === index ? { ...item, ...patch } : item) }));
  const removeItem = (index: number) => {
    setState((current) => ({ ...current, items: current.items.filter((_, position) => position !== index) }));
    setOpenLine(null);
    setValidation(null);
  };
  const addItem = (kind: ReceiptLineKind) => {
    update({
      items: [...state.items, {
        key: nextKey(), id: null, name: "", quantity: kind === "product" ? 1 : null, unit: null, unitPrice: null, lineTotal: null, kind,
        productLink: "auto", productId: null, productName: null, productNeedsReview: false, boundingBox: null
      }]
    });
    setOpenLine(state.items.length);
  };

  const submit = () => {
    if (state.items.length === 0) { setValidation(t("purchaseEdit.needLine")); return; }
    if (state.items.some((item) => !item.name.trim())) { setValidation(t("purchaseEdit.nameRequired")); return; }
    save.mutate();
  };

  const currency = state.currency.trim().toUpperCase() || null;
  const conflict = save.error instanceof ApiError && save.error.status === 409;

  return (
    <Page
      title={t("purchaseEdit.edit")}
      largeTitle={false}
      start={<IonButton disabled={save.isPending} onClick={() => onDone(false)}>{t("ui.cancel")}</IonButton>}
      end={<ActionButton strong loading={save.isPending} onClick={submit}>{save.isPending ? t("purchaseEdit.saving") : t("purchaseEdit.saveShort")}</ActionButton>}
    >
      <IonList inset>
        <IonItem>
          <IonInput ref={merchantRef} label={t("purchaseEdit.store")} labelPlacement="fixed" value={state.merchantName} onIonInput={(event) => update({ merchantName: String(event.detail.value ?? "") })} />
        </IonItem>
        <DateField label={t("purchaseEdit.dateTime")} withTime formValue value={state.purchasedAt} emptyText={t("purchaseEdit.unknownDate")} onChange={(purchasedAt) => update({ purchasedAt })} />
        <IonItem>
          <IonInput label={t("purchaseEdit.currency")} labelPlacement="fixed" value={state.currency} maxlength={3} autocapitalize="characters" onIonInput={(event) => update({ currency: String(event.detail.value ?? "").toUpperCase() })} />
        </IonItem>
        <IonItem lines="none">
          <DecimalInput inputRef={totalRef} label={t("purchaseEdit.total")} value={state.receiptTotal} onChange={(receiptTotal) => update({ receiptTotal })} />
          {currency && <IonNote slot="end">{currency}</IonNote>}
        </IonItem>
      </IonList>

      <Reconciliation items={state.items} receiptTotal={state.receiptTotal} currency={currency} />

      <LinesEditor
        title={t("purchaseEdit.linesCount", { count: state.items.filter((item) => !isDiscount(item)).length })}
        help={<HelpLink topic="purchase-edit" />}
        lines={state.items}
        currency={currency}
        openLine={openLine}
        onOpenLine={(index) => { setOpenLine(index); if (index === null) setPickerOpenFor(null); }}
        onChange={(index, patch) => { updateItem(index, patch); setValidation(null); }}
        onRemove={removeItem}
        onAdd={addItem}
        addKinds={["product", "discount"]}
        focusField={lineFocus}
        photo={receiptPhoto.viewer}
        describe={(line) => (
          <p>
            <ProductLinkNote
              text={productStatusText(t, line)}
              lineName={line.name}
              productName={line.productLink === "existing" ? line.productName : null}
              flagged={line.productLink === "existing" && line.productNeedsReview}
              isNew={line.productLink === "new"}
            />
          </p>
        )}
        sheetExtra={(line, index) => (
          <ProductLinkRows
            line={line}
            number={index + 1}
            pickerOpen={pickerOpenFor === index}
            onPickerOpenChange={(open) => setPickerOpenFor(open ? index : null)}
            onChoose={(choice) => updateItem(index, { ...choice, productNeedsReview: Boolean(choice.productNeedsReview) })}
          />
        )}
      />

      {validation && <Notice tone="danger" role="alert"><p>{validation}</p></Notice>}
      {save.isError && (
        <Notice tone="danger" role="alert">
          <p>{conflict ? t("purchaseEdit.conflict") : t("purchaseEdit.saveError", { message: save.error instanceof Error ? save.error.message : "" })}</p>
          {conflict && <IonButton fill="outline" size="small" onClick={onReload}>{t("purchaseEdit.reload")}</IonButton>}
        </Notice>
      )}
    </Page>
  );
}

/** Live «do the lines add up?» with the same tolerance and discount rule as the backend. */
function Reconciliation({ items, receiptTotal, currency }: { items: EditLine[]; receiptTotal: number | null; currency: string | null }) {
  const { t, i18n } = useTranslation();
  const money = (value: number) => formatMoney(value, currency, i18n.language);
  const linesTotal = items.reduce((sum, item) => sum + (item.lineTotal ?? 0), 0);
  const missing = items.filter((item) => item.lineTotal === null).length;
  const difference = receiptTotal === null ? null : linesTotal - receiptTotal;
  const matches = difference !== null && Math.abs(difference) <= moneyComparisonTolerance;
  const tone = receiptTotal === null ? "neutral" : matches ? "success" : "warning";
  return (
    <div role="status" aria-live="polite">
      <Footnote tone={tone}>
        {receiptTotal === null
          ? t("purchaseEdit.reconcileNoTotal", { lines: money(linesTotal) })
          : matches
            ? t("purchaseEdit.reconcileOk", { lines: money(linesTotal) })
            : t("purchaseEdit.reconcileMismatch", { lines: money(linesTotal), total: money(receiptTotal), difference: money(difference!) })}
        {missing > 0 && <><br />{t("purchaseEdit.reconcileMissing", { count: missing })}</>}
      </Footnote>
    </div>
  );
}

function parseFocus(focus: string | null): { kind: "total" } | { kind: "price" | "product"; line: number } | null {
  if (focus === "total") return { kind: "total" };
  const match = /^(price|product)-(\d+)$/.exec(focus ?? "");
  return match ? { kind: match[1] as "price" | "product", line: Number(match[2]) } : null;
}

function toLocalInput(value: string | null): string {
  if (!value) return "";
  const date = new Date(value);
  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
}
