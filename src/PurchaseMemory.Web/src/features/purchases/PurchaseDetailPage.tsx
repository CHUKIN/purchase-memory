import { IonButton, IonInput, IonItem, IonLabel, IonList, IonNote } from "@ionic/react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useLocation, useNavigate, useParams, useSearchParams } from "react-router";
import { ApiError, getPurchase, updatePurchase } from "../../api/client";
import type {
  BoundingBox,
  ProductLinkChoice,
  PurchaseDetail,
  PurchaseProductCandidate,
  ReceiptLineKind,
  UpdatePurchaseRequest
} from "../../api/types";
import { useAuthorizedImageUrl } from "../../auth/useAuthorizedImageUrl";
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
import { HelpLink } from "../help/HelpLink";
import { formatMoney } from "./money";
import { ProductLinkRows, productStatusText } from "./ProductLinkRows";

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
  const photoUrl = purchase.sourceReceiptId ? `/api/receipts/${purchase.sourceReceiptId}/image` : null;
  const photo = useAuthorizedImageUrl(photoUrl);
  const returnState = useReturnState();

  return (
    <Page
      title={purchase.merchantName ?? t("purchaseEdit.unknownStore")}
      back={back}
      end={<IonButton strong onClick={onEdit}>{t("purchaseEdit.edit")}</IonButton>}
    >
      <IonList inset>
        <IonItem>
          <IonLabel>{t("purchaseEdit.dateTime")}</IonLabel>
          <IonNote slot="end" className="pm-pick-value">{purchase.purchasedAt ? formatDateTime(purchase.purchasedAt, locale) : t("purchaseEdit.unknownDate")}</IonNote>
        </IonItem>
        <IonItem lines={purchase.sourceReceiptId ? undefined : "none"}>
          <IonLabel>{t("purchaseEdit.total")}</IonLabel>
          <IonNote slot="end" className="pm-amount pm-total">{money(purchase.receiptTotal)}</IonNote>
        </IonItem>
        {purchase.sourceReceiptId && (
          <ItemLink to={`/receipts/${purchase.sourceReceiptId}`} state={returnState} lines="none">
            <IonLabel color="primary">{t("purchaseEdit.openReceipt")}</IonLabel>
          </ItemLink>
        )}
      </IonList>

      <section aria-labelledby="purchase-issues">
        <SectionHeader id="purchase-issues">{t("purchaseEdit.issuesTitle")}</SectionHeader>
        {purchase.issues.length === 0
          ? <Footnote>{t("purchaseEdit.noIssues")}</Footnote>
          : (
            <IonList inset className="pm-after-header">
              {purchase.issues.map((issue) => (
                <IonItem key={issueKey(issue)}>
                  <IonLabel className="pm-wrap pm-warn-text">{issueCopy(t, issue).title}</IonLabel>
                </IonItem>
              ))}
            </IonList>
          )}
      </section>

      <section aria-labelledby="purchase-lines">
        <SectionHeader id="purchase-lines">{t("purchaseEdit.lines")} · {t("purchaseEdit.linesCount", { count: productCount })}</SectionHeader>
        <IonList inset className="pm-after-header">
          {purchase.items.map((item, index) => {
            if (isDiscount(item)) {
              const target = links[index].target;
              return (
                <IonItem key={item.position}>
                  <ReceiptDiscountLine
                    name={item.name}
                    lineTotal={item.lineTotal}
                    currency={purchase.currency}
                    productName={target === null ? null : purchase.items[target].name}
                    productLineTotal={target === null ? null : purchase.items[target].lineTotal}
                    appliesToReceipt={links[index].appliesToReceipt}
                  />
                </IonItem>
              );
            }
            return (
              <IonItem key={item.position}>
                <IonLabel className="pm-wrap">
                  <h3>{item.name}</h3>
                  <p className={item.pendingMatch ? "pm-warn-text" : undefined}>
                    {item.productName
                      ? t("purchaseEdit.productLinked", { name: item.productName })
                      : item.pendingMatch ? t("purchaseEdit.productPending") : t("purchaseEdit.productUnlinked")}
                  </p>
                  {item.unitPrice === null && <p className="pm-warn-text">{t("purchaseEdit.missingPrice")}</p>}
                  {impressions.data && <PurchaseLineImpressions item={item} impressions={impressions.data} locale={locale} />}
                </IonLabel>
                <IonNote slot="end" className="pm-amount">{money(item.lineTotal)}</IonNote>
              </IonItem>
            );
          })}
        </IonList>
      </section>

      <section aria-label={t("purchaseEdit.photoAlt")}>
        <SectionHeader>{t("purchaseEdit.photoAlt")}</SectionHeader>
        {!photoUrl && <Footnote>{t("purchaseEdit.noPhoto")}</Footnote>}
        {photoUrl && photo.src && <div className="pm-photo-card"><img className="pm-photo" src={photo.src} alt={t("purchaseEdit.photoAlt")} /></div>}
        {photoUrl && photo.failed && <Footnote>{t("purchaseEdit.photoFailed")}</Footnote>}
      </section>

      <Footnote><HelpLink topic="purchase-edit" /></Footnote>
    </Page>
  );
}

interface EditLine {
  key: string;
  name: string;
  quantity: number | null;
  unit: string | null;
  unitPrice: number | null;
  lineTotal: number | null;
  kind: ReceiptLineKind;
  productLink: ProductLinkChoice;
  productId: string | null;
  productName: string | null;
  pendingMatch: boolean;
  candidates: PurchaseProductCandidate[];
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
      name: item.name,
      quantity: item.quantity,
      unit: item.unit,
      unitPrice: item.unitPrice,
      lineTotal: item.lineTotal,
      kind: item.kind,
      productLink: item.productId ? "existing" : "auto",
      productId: item.productId,
      productName: item.productName,
      pendingMatch: item.pendingMatch,
      candidates: item.candidates,
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
      boundingBox: item.boundingBox
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
    mutationFn: () => updatePurchase(purchase.id, toRequest(purchase, state)),
    onSuccess: async (updated) => {
      queryClient.setQueryData(purchaseQueryKey(purchase.id), updated);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: reviewQueueQueryKey }),
        queryClient.invalidateQueries({ queryKey: ["purchases", "feed"] }),
        queryClient.invalidateQueries({ queryKey: ["products"] }),
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
        key: nextKey(), name: "", quantity: kind === "product" ? 1 : null, unit: null, unitPrice: null, lineTotal: null, kind,
        productLink: "auto", productId: null, productName: null, pendingMatch: false, candidates: [], boundingBox: null
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
      <Footnote>{t("purchaseEdit.editHint")}</Footnote>
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
        title={`${t("purchaseEdit.lines")} · ${t("purchaseEdit.linesCount", { count: state.items.filter((item) => !isDiscount(item)).length })}`}
        lines={state.items}
        currency={currency}
        openLine={openLine}
        onOpenLine={(index) => { setOpenLine(index); if (index === null) setPickerOpenFor(null); }}
        onChange={(index, patch) => { updateItem(index, patch); setValidation(null); }}
        onRemove={removeItem}
        onAdd={addItem}
        addKinds={["product", "discount"]}
        focusField={lineFocus}
        describe={(line) => (
          <p className={line.pendingMatch && line.productLink === "auto" ? "pm-warn-text" : undefined}>{productStatusText(t, line)}</p>
        )}
        sheetExtra={(line, index) => (
          <ProductLinkRows
            line={line}
            number={index + 1}
            pickerOpen={pickerOpenFor === index}
            onPickerOpenChange={(open) => setPickerOpenFor(open ? index : null)}
            onChoose={(choice) => updateItem(index, choice)}
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
      <Footnote><HelpLink topic="purchase-edit" /></Footnote>
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

function formatDateTime(value: string, locale: string) {
  return new Intl.DateTimeFormat(locale, { dateStyle: "medium", timeStyle: "short" }).format(new Date(value));
}
