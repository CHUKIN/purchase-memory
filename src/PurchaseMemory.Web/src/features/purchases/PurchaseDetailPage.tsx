import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Pencil, Plus, Trash2 } from "lucide-react";
import { useEffect, useId, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { ApiError, getProductCatalog, getPurchase, updatePurchase } from "../../api/client";
import type {
  BoundingBox,
  ProductLinkChoice,
  PurchaseDetail,
  PurchaseProductCandidate,
  ReceiptLineKind,
  UpdatePurchaseRequest
} from "../../api/types";
import { useAuthorizedImageUrl } from "../../auth/useAuthorizedImageUrl";
import { Button, IconButton, SkeletonCard, Surface } from "../../design-system/components";
import { issueCopy, issueKey, moneyComparisonTolerance } from "../issues/issueText";
import { discountLinks, isDiscount } from "../receipts/discountLines";
import { ReceiptDiscountLine } from "../receipts/ReceiptDiscountLine";
import { reviewQueueQueryKey } from "../review-queue/reviewQueueQueries";
import { PurchaseLineImpressions, usePurchaseImpressions } from "../impressions/ImpressionSections";
import { formatMoney } from "./money";
import { HelpLink } from "../help/HelpLink";
import styles from "./PurchaseDetailPage.module.css";

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
  const [savedNotice, setSavedNotice] = useState(false);
  const query = useQuery({
    queryKey: purchaseQueryKey(purchaseId),
    queryFn: () => getPurchase(purchaseId),
    enabled: purchaseId.length > 0,
    retry: (count, error) => !(error instanceof ApiError && error.status === 404) && count < 2
  });
  const editing = searchParameters.get("edit") === "1";
  const focus = searchParameters.get("focus");
  const photoUrl = query.data?.sourceReceiptId ? `/api/receipts/${query.data.sourceReceiptId}/image` : null;
  const photo = useAuthorizedImageUrl(photoUrl);

  const startEditing = () => { setSavedNotice(false); setSearchParameters({ edit: "1" }); };
  const stopEditing = (saved: boolean) => { setSavedNotice(saved); setSearchParameters({}); };

  if (query.isPending) {
    return <div className={styles.page}><div role="status" aria-label={t("purchaseEdit.loading")}><SkeletonCard /></div></div>;
  }

  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404;
    return (
      <div className={styles.page}>
        <Surface className={styles.notice} role="alert">
          <p>{notFound ? t("purchaseEdit.notFound") : t("purchaseEdit.loadError")}</p>
          {!notFound && <Button intent="secondary" onClick={() => void query.refetch()}>{t("purchaseEdit.retry")}</Button>}
          <Link className={styles.link} to="/purchases">{t("purchaseEdit.backToPurchases")}</Link>
        </Surface>
      </div>
    );
  }

  const purchase = query.data;
  return (
    <div className={styles.page}>
      <header className={styles.hero}>
        <p className={styles.eyebrow}>{t("purchaseEdit.eyebrow")}</p>
        <h1>{editing ? t("purchaseEdit.editTitle") : purchase.merchantName ?? t("purchaseEdit.unknownStore")}</h1>
        <nav className={styles.heroLinks}>
          <Link className={styles.back} to="/purchases"><ArrowLeft aria-hidden="true" />{t("purchaseEdit.backToPurchases")}</Link>
          {purchase.sourceReceiptId && <Link className={styles.link} to={`/receipts/${purchase.sourceReceiptId}`}>{t("purchaseEdit.openReceipt")}</Link>}
          <HelpLink topic="purchase-edit" />
        </nav>
      </header>

      <div className={styles.layout}>
        <div className={styles.main}>
          {savedNotice && !editing && <p className={styles.success} role="status">{t("purchaseEdit.saved")}</p>}
          {editing
            ? <PurchaseEditor key={purchase.updatedAt} purchase={purchase} focus={focus} onDone={stopEditing} onReload={() => void query.refetch()} />
            : <PurchaseSummary purchase={purchase} onEdit={startEditing} />}
        </div>
        <aside className={styles.photo} aria-label={t("purchaseEdit.photoAlt")}>
          {!photoUrl && <p className={styles.muted}>{t("purchaseEdit.noPhoto")}</p>}
          {photoUrl && photo.src && <img src={photo.src} alt={t("purchaseEdit.photoAlt")} />}
          {photoUrl && photo.failed && <p className={styles.muted}>{t("purchaseEdit.photoFailed")}</p>}
        </aside>
      </div>
    </div>
  );
}

function PurchaseSummary({ purchase, onEdit }: { purchase: PurchaseDetail; onEdit: () => void }) {
  const { t, i18n } = useTranslation();
  const locale = i18n.language;
  const money = (value: number | null) => value === null ? "—" : formatMoney(value, purchase.currency, locale);
  const productCount = purchase.items.filter((item) => !isDiscount(item)).length;
  // Optional context: if it fails to load the purchase is still fully usable.
  const impressions = usePurchaseImpressions(purchase.id);
  return (
    <Surface className={styles.card}>
      <div className={styles.summaryHeader}>
        <div>
          <p className={styles.muted}>{purchase.purchasedAt ? formatDateTime(purchase.purchasedAt, locale) : t("purchaseEdit.unknownDate")}</p>
          <p className={styles.total}>{money(purchase.receiptTotal)}</p>
        </div>
        <Button intent="primary" leftSection={<Pencil aria-hidden="true" size={17} />} onClick={onEdit}>{t("purchaseEdit.edit")}</Button>
      </div>

      <section aria-labelledby="purchase-issues">
        <h2 id="purchase-issues" className={styles.sectionTitle}>{t("purchaseEdit.issuesTitle")}</h2>
        {purchase.issues.length === 0
          ? <p className={styles.muted}>{t("purchaseEdit.noIssues")}</p>
          : <ul className={styles.issues}>{purchase.issues.map((issue) => <li key={issueKey(issue)}>{issueCopy(t, issue).title}</li>)}</ul>}
      </section>

      <section aria-labelledby="purchase-lines">
        <h2 id="purchase-lines" className={styles.sectionTitle}>{t("purchaseEdit.lines")} · {t("purchaseEdit.linesCount", { count: productCount })}</h2>
        <ul className={styles.lines}>
          {purchase.items.map((item) => (
            <li key={item.position} className={isDiscount(item) ? styles.discount : undefined}>
              <div className={styles.lineName}>
                <strong>{item.name}</strong>
                {!isDiscount(item) && (
                  <span className={item.pendingMatch ? styles.warningText : styles.muted}>
                    {item.productName
                      ? t("purchaseEdit.productLinked", { name: item.productName })
                      : item.pendingMatch ? t("purchaseEdit.productPending") : t("purchaseEdit.productUnlinked")}
                  </span>
                )}
              </div>
              <span className={styles.amount}>
                {item.unitPrice === null && !isDiscount(item) && <span className={styles.badge}>{t("purchaseEdit.missingPrice")}</span>}
                {money(item.lineTotal)}
              </span>
              {!isDiscount(item) && impressions.data && (
                <div className={styles.lineImpressions}>
                  <PurchaseLineImpressions item={item} impressions={impressions.data} locale={locale} />
                </div>
              )}
            </li>
          ))}
        </ul>
      </section>
    </Surface>
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
  const idPrefix = useId();
  const [state, setState] = useState(() => toEditState(purchase));
  const [validation, setValidation] = useState<string | null>(null);
  const [openPicker, setOpenPicker] = useState<number | null>(() => {
    const target = parseFocus(focus);
    return target?.kind === "product" ? target.line : null;
  });
  const focusedOnce = useRef(false);

  useEffect(() => {
    if (focusedOnce.current) return;
    focusedOnce.current = true;
    const target = parseFocus(focus);
    const id = target === null
      ? `${idPrefix}-merchant`
      : target.kind === "total" ? `${idPrefix}-total` : `${idPrefix}-${target.kind}-${target.line}`;
    const element = document.getElementById(id) ?? document.getElementById(`${idPrefix}-merchant`);
    element?.focus();
    element?.scrollIntoView?.({ block: "center" });
  }, [focus, idPrefix]);

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
    update({ items: state.items.map((item, position) => position === index ? { ...item, ...patch } : item) });
  const removeItem = (index: number) => {
    update({ items: state.items.filter((_, position) => position !== index) });
    setOpenPicker(null);
  };
  const addItem = (kind: ReceiptLineKind) => update({
    items: [...state.items, {
      key: nextKey(), name: "", quantity: kind === "product" ? 1 : null, unit: null, unitPrice: null, lineTotal: null, kind,
      productLink: "auto", productId: null, productName: null, pendingMatch: false, candidates: [], boundingBox: null
    }]
  });

  const submit = () => {
    if (state.items.length === 0) { setValidation(t("purchaseEdit.needLine")); return; }
    if (state.items.some((item) => !item.name.trim())) { setValidation(t("purchaseEdit.nameRequired")); return; }
    save.mutate();
  };

  const links = discountLinks(state.items);
  const currency = state.currency.trim().toUpperCase() || null;
  const conflict = save.error instanceof ApiError && save.error.status === 409;

  return (
    <Surface className={styles.card}>
      <p className={styles.muted}>{t("purchaseEdit.editHint")}</p>
      <form className={styles.form} onSubmit={(event) => { event.preventDefault(); submit(); }} noValidate>
        <div className={styles.headerFields}>
          <TextInput id={`${idPrefix}-merchant`} label={t("purchaseEdit.store")} value={state.merchantName} onChange={(merchantName) => update({ merchantName })} />
          <TextInput id={`${idPrefix}-date`} type="datetime-local" label={t("purchaseEdit.dateTime")} value={state.purchasedAt} onChange={(purchasedAt) => update({ purchasedAt })} />
          <TextInput id={`${idPrefix}-currency`} label={t("purchaseEdit.currency")} value={state.currency} maxLength={3} onChange={(value) => update({ currency: value.toUpperCase() })} />
          <NumberInput id={`${idPrefix}-total`} label={t("purchaseEdit.total")} value={state.receiptTotal} onChange={(receiptTotal) => update({ receiptTotal })} />
        </div>

        <Reconciliation items={state.items} receiptTotal={state.receiptTotal} currency={currency} />

        <fieldset className={styles.linesFieldset}>
          <legend className={styles.sectionTitle}>{t("purchaseEdit.lines")}</legend>
          {state.items.map((item, index) => {
            const number = index + 1;
            if (isDiscount(item)) {
              const target = links[index].target;
              return (
                <div key={item.key} className={styles.discountEditor} role="group" aria-label={t("purchaseEdit.discountLine", { number })}>
                  <ReceiptDiscountLine
                    name={item.name}
                    lineTotal={item.lineTotal}
                    currency={currency}
                    productName={target === null ? null : state.items[target].name || null}
                    productLineTotal={target === null ? null : state.items[target].lineTotal}
                    appliesToReceipt={links[index].appliesToReceipt}
                    onChange={(change) => updateItem(index, change)}
                    onDelete={() => removeItem(index)}
                  />
                </div>
              );
            }
            const missingPrice = item.unitPrice === null;
            return (
              <article key={item.key} className={styles.lineEditor} aria-label={t("purchaseEdit.line", { number })}>
                <div className={styles.lineHeader}>
                  <span>{t("purchaseEdit.line", { number })}</span>
                  {missingPrice && <span className={styles.badge}>{t("purchaseEdit.missingPrice")}</span>}
                  <IconButton className={styles.remove} intent="danger" icon={<Trash2 />} label={t("purchaseEdit.removeLine", { number })} onClick={() => removeItem(index)} />
                </div>
                <TextInput id={`${idPrefix}-name-${index}`} label={t("purchaseEdit.name")} value={item.name} required onChange={(name) => updateItem(index, { name })} />
                <div className={styles.numbers}>
                  <NumberInput id={`${idPrefix}-quantity-${index}`} label={t("purchaseEdit.quantity")} step="0.001" value={item.quantity} onChange={(quantity) => updateItem(index, { quantity })} />
                  <NumberInput id={`${idPrefix}-price-${index}`} label={t("purchaseEdit.unitPrice")} value={item.unitPrice} invalid={missingPrice} onChange={(unitPrice) => updateItem(index, { unitPrice })} />
                  <NumberInput id={`${idPrefix}-lineTotal-${index}`} label={t("purchaseEdit.lineTotal")} value={item.lineTotal} onChange={(lineTotal) => updateItem(index, { lineTotal })} />
                </div>
                <ProductLink
                  id={`${idPrefix}-product-${index}`}
                  number={number}
                  line={item}
                  open={openPicker === index}
                  onToggle={() => setOpenPicker(openPicker === index ? null : index)}
                  onChoose={(choice) => { updateItem(index, choice); setOpenPicker(null); }}
                />
              </article>
            );
          })}
        </fieldset>

        <div className={styles.addButtons}>
          <Button type="button" intent="secondary" leftSection={<Plus aria-hidden="true" size={17} />} onClick={() => addItem("product")}>{t("purchaseEdit.addLine")}</Button>
          <Button type="button" intent="quiet" leftSection={<Plus aria-hidden="true" size={17} />} onClick={() => addItem("discount")}>{t("purchaseEdit.addDiscount")}</Button>
        </div>

        {validation && <p className={styles.error} role="alert">{validation}</p>}
        {save.isError && (
          <div className={styles.error} role="alert">
            <p>{conflict ? t("purchaseEdit.conflict") : t("purchaseEdit.saveError", { message: save.error instanceof Error ? save.error.message : "" })}</p>
            {conflict && <Button type="button" intent="secondary" onClick={onReload}>{t("purchaseEdit.reload")}</Button>}
          </div>
        )}

        <div className={styles.actions}>
          <Button type="submit" intent="primary" loading={save.isPending}>{save.isPending ? t("purchaseEdit.saving") : t("purchaseEdit.save")}</Button>
          <Button type="button" intent="secondary" disabled={save.isPending} onClick={() => onDone(false)}>{t("purchaseEdit.cancel")}</Button>
        </div>
      </form>
    </Surface>
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
  const tone = receiptTotal === null ? styles.reconcileNeutral : matches ? styles.reconcileOk : styles.reconcileMismatch;
  return (
    <div className={`${styles.reconcile} ${tone}`} role="status" aria-live="polite">
      <p>
        {receiptTotal === null
          ? t("purchaseEdit.reconcileNoTotal", { lines: money(linesTotal) })
          : matches
            ? t("purchaseEdit.reconcileOk", { lines: money(linesTotal) })
            : t("purchaseEdit.reconcileMismatch", { lines: money(linesTotal), total: money(receiptTotal), difference: money(difference!) })}
      </p>
      {missing > 0 && <p>{t("purchaseEdit.reconcileMissing", { count: missing })}</p>}
    </div>
  );
}

type LinkChoice = Pick<EditLine, "productLink" | "productId" | "productName">;

function ProductLink({ id, number, line, open, onToggle, onChoose }: {
  id: string;
  number: number;
  line: EditLine;
  open: boolean;
  onToggle: () => void;
  onChoose: (choice: LinkChoice) => void;
}) {
  const { t } = useTranslation();
  const panelId = `${id}-panel`;
  const status = line.productLink === "existing" && line.productName
    ? t("purchaseEdit.productLinked", { name: line.productName })
    : line.productLink === "new"
      ? t("purchaseEdit.productNew", { name: line.name.trim() || "…" })
      : line.pendingMatch ? t("purchaseEdit.productPending") : t("purchaseEdit.productAuto");
  const pending = line.pendingMatch && line.productLink === "auto";
  return (
    <div className={styles.product}>
      <div className={styles.productStatus}>
        <span className={styles.fieldLabel}>{t("purchaseEdit.product")}</span>
        <span className={pending ? styles.warningText : undefined}>{status}</span>
      </div>
      <button
        id={id}
        type="button"
        className={styles.link}
        aria-expanded={open}
        aria-controls={panelId}
        aria-label={t("purchaseEdit.chooseProduct", { number })}
        onClick={onToggle}
      >
        {t("purchaseEdit.changeProduct")}
      </button>
      {open && <ProductPicker id={panelId} number={number} line={line} onChoose={onChoose} onClose={onToggle} />}
    </div>
  );
}

function ProductPicker({ id, number, line, onChoose, onClose }: { id: string; number: number; line: EditLine; onChoose: (choice: LinkChoice) => void; onClose: () => void }) {
  const { t } = useTranslation();
  const searchId = useId();
  const [text, setText] = useState("");
  const debounced = useDebounced(text.trim(), 250);
  const search = useQuery({
    queryKey: ["products", "catalog", "picker", debounced],
    queryFn: () => getProductCatalog({ query: debounced, pageSize: 8 }),
    enabled: debounced.length >= 2
  });
  const results = useMemo(() => search.data?.items ?? [], [search.data]);
  const choose = (productId: string, productName: string) => onChoose({ productLink: "existing", productId, productName });

  return (
    <div id={id} className={styles.picker} role="group" aria-label={t("purchaseEdit.chooseProduct", { number })}>
      {line.candidates.length > 0 && (
        <div>
          <p className={styles.fieldLabel}>{t("purchaseEdit.candidates")}</p>
          <ul className={styles.options}>
            {line.candidates.map((candidate) => (
              <li key={candidate.productId}>
                <button type="button" aria-pressed={line.productId === candidate.productId} onClick={() => choose(candidate.productId, candidate.name)}>
                  {t("purchaseEdit.useProduct", { name: candidate.name })}
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}
      <label className={styles.fieldLabel} htmlFor={searchId}>{t("purchaseEdit.searchProduct")}</label>
      <input id={searchId} className={styles.input} type="search" value={text} placeholder={t("purchaseEdit.searchPlaceholder")} onChange={(event) => setText(event.target.value)} />
      <div aria-live="polite">
        {search.isFetching && <p className={styles.muted}>{t("purchaseEdit.searching")}</p>}
        {search.isError && <p className={styles.warningText}>{t("purchaseEdit.searchError")}</p>}
        {search.isSuccess && results.length === 0 && <p className={styles.muted}>{t("purchaseEdit.noResults")}</p>}
      </div>
      {results.length > 0 && (
        <ul className={styles.options}>
          {results.map((product) => (
            <li key={product.id}>
              <button type="button" aria-pressed={line.productId === product.id} onClick={() => choose(product.id, product.name)}>
                {t("purchaseEdit.useProduct", { name: product.name })}
              </button>
            </li>
          ))}
        </ul>
      )}
      <div className={styles.pickerActions}>
        <Button type="button" intent="secondary" onClick={() => onChoose({ productLink: "new", productId: null, productName: null })}>{t("purchaseEdit.createProduct")}</Button>
        <Button type="button" intent="quiet" onClick={() => onChoose({ productLink: "auto", productId: null, productName: null })}>{t("purchaseEdit.autoProduct")}</Button>
        <Button type="button" intent="quiet" onClick={onClose}>{t("purchaseEdit.closeProduct")}</Button>
      </div>
    </div>
  );
}

function TextInput({ id, label, value, onChange, type = "text", required, maxLength }: {
  id: string; label: string; value: string; onChange: (value: string) => void; type?: string; required?: boolean; maxLength?: number;
}) {
  return (
    <label className={styles.field} htmlFor={id}>
      <span className={styles.fieldLabel}>{label}</span>
      <input id={id} className={styles.input} type={type} value={value} required={required} maxLength={maxLength}
        aria-invalid={required && !value.trim() ? true : undefined} onChange={(event) => onChange(event.target.value)} />
    </label>
  );
}

function NumberInput({ id, label, value, onChange, step = "0.01", invalid }: {
  id: string; label: string; value: number | null; onChange: (value: number | null) => void; step?: string; invalid?: boolean;
}) {
  return (
    <label className={styles.field} htmlFor={id}>
      <span className={styles.fieldLabel}>{label}</span>
      <input id={id} className={styles.input} inputMode="decimal" type="number" min="0" step={step} value={value ?? ""}
        aria-invalid={invalid || undefined}
        onChange={(event) => onChange(event.target.value === "" ? null : Number(event.target.value))} />
    </label>
  );
}

function useDebounced<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(timer);
  }, [value, delayMs]);
  return debounced;
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
