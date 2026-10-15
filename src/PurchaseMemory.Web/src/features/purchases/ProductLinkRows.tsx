import { IonButton, IonButtons, IonContent, IonHeader, IonItem, IonLabel, IonList, IonModal, IonSearchbar, IonTitle, IonToolbar } from "@ionic/react";
import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, Check, Package, PackagePlus, PackageX } from "lucide-react";
import { useEffect, useId, useMemo, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { getProductCatalog } from "../../api/client";
import type { ProductLinkChoice, PurchaseProductCandidate } from "../../api/types";
import type { TFunction } from "i18next";
import { Footnote, SectionHeader } from "../../design-system/components";

/** The status of a line's catalog product, as one short sentence. */
export function productStatusText(t: TFunction, line: Pick<LinkableLine, "productLink" | "productName" | "name" | "pendingMatch">) {
  return line.productLink === "existing" && line.productName
    ? t("purchaseEdit.productLinked", { name: line.productName })
    : line.productLink === "new"
      ? t("purchaseEdit.productNew", { name: line.name.trim() || "…" })
      : line.pendingMatch ? t("purchaseEdit.productPending") : t("purchaseEdit.productAuto");
}

const sameName = (a: string, b: string) => a.trim().toLocaleLowerCase() === b.trim().toLocaleLowerCase();

/**
 * A line's catalog product in a list row, icon first: ⚠ «Выберите товар» when
 * matching was unsure, the product's name when it differs from the line's,
 * «Новый товар» for a new one, «Без товара» for a saved line left unlinked,
 * and nothing more when it is obvious. The full
 * sentence (`text`, e.g. «Связано с «…»») is always there for screen readers.
 */
export function ProductLinkNote({ text, lineName, productName, pending, isNew = false, unlinked = false }: {
  text: string;
  lineName: string;
  productName: string | null;
  pending: boolean;
  isNew?: boolean;
  unlinked?: boolean;
}) {
  const { t } = useTranslation();
  let visible: ReactNode = null;
  if (pending) visible = <><AlertTriangle size={13} className="pm-inline-icon" />{t("purchaseEdit.productPendingShort")}</>;
  else if (isNew) visible = <><PackagePlus size={13} className="pm-inline-icon" />{t("purchaseEdit.productNewShort")}</>;
  else if (unlinked) visible = <><PackageX size={13} className="pm-inline-icon" />{t("purchaseEdit.productUnlinkedShort")}</>;
  else if (productName && !sameName(productName, lineName)) visible = <><Package size={13} className="pm-inline-icon" />{productName}</>;
  return (
    <span className="pm-line-detail pm-link-note" data-warn={pending}>
      {visible && <span aria-hidden="true">{visible}</span>}
      <span className="visually-hidden">{text}</span>
    </span>
  );
}

export interface LinkableLine {
  name: string;
  productLink: ProductLinkChoice;
  productId: string | null;
  productName: string | null;
  pendingMatch: boolean;
  candidates: PurchaseProductCandidate[];
}

export type LinkChoice = Pick<LinkableLine, "productLink" | "productId" | "productName">;

/**
 * «Товар в каталоге» inside a purchase line's sheet: what the line is linked
 * to, and «Изменить товар», which opens a full-screen picker with the
 * matcher's guesses, a search over the catalog, a new product, or «decide
 * automatically after saving».
 */
export function ProductLinkRows({ line, number, pickerOpen, onPickerOpenChange, onChoose }: {
  line: LinkableLine;
  number: number;
  pickerOpen: boolean;
  onPickerOpenChange: (open: boolean) => void;
  onChoose: (choice: LinkChoice) => void;
}) {
  const { t } = useTranslation();
  const pending = line.pendingMatch && line.productLink === "auto";
  return (
    <>
      <SectionHeader level={3}>{t("purchaseEdit.product")}</SectionHeader>
      <IonList inset className="pm-after-header">
        <IonItem>
          <IonLabel className={["pm-wrap", pending ? "pm-warn-text" : ""].filter(Boolean).join(" ")}>{productStatusText(t, line)}</IonLabel>
        </IonItem>
        <IonItem button detail lines="none" onClick={() => onPickerOpenChange(true)} aria-label={t("purchaseEdit.chooseProduct", { number })}>
          <IonLabel color="primary">{t("purchaseEdit.changeProduct")}</IonLabel>
        </IonItem>
      </IonList>
      <ProductPicker
        isOpen={pickerOpen}
        number={number}
        line={line}
        onClose={() => onPickerOpenChange(false)}
        onChoose={(choice) => { onChoose(choice); onPickerOpenChange(false); }}
      />
    </>
  );
}

function ProductPicker({ isOpen, number, line, onChoose, onClose }: {
  isOpen: boolean;
  number: number;
  line: LinkableLine;
  onChoose: (choice: LinkChoice) => void;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const titleId = useId();
  const [text, setText] = useState("");
  const debounced = useDebounced(text.trim(), 250);
  const search = useQuery({
    queryKey: ["products", "catalog", "picker", debounced],
    queryFn: () => getProductCatalog({ query: debounced, pageSize: 8 }),
    enabled: isOpen && debounced.length >= 2
  });
  const results = useMemo(() => search.data?.items ?? [], [search.data]);
  const choose = (productId: string, productName: string) => onChoose({ productLink: "existing", productId, productName });
  const row = (productId: string, name: string) => {
    const selected = line.productLink === "existing" && line.productId === productId;
    return (
      <IonItem key={productId} button detail={false} onClick={() => choose(productId, name)} aria-label={t("purchaseEdit.useProduct", { name })} aria-current={selected || undefined}>
        <IonLabel className="pm-wrap">{name}</IonLabel>
        {selected && <Check slot="end" size={20} className="pm-check" aria-hidden="true" />}
      </IonItem>
    );
  };

  return (
    <IonModal isOpen={isOpen} onDidDismiss={() => { setText(""); onClose(); }} aria-labelledby={titleId}>
      <IonHeader>
        <IonToolbar>
          <IonButtons slot="start"><IonButton onClick={onClose}>{t("ui.cancel")}</IonButton></IonButtons>
          <IonTitle><h2 id={titleId} className="pm-title-heading">{t("purchaseEdit.chooseProduct", { number })}</h2></IonTitle>
        </IonToolbar>
        <IonToolbar>
          <IonSearchbar value={text} placeholder={t("purchaseEdit.searchPlaceholder")} aria-label={t("purchaseEdit.searchProduct")} onIonInput={(event) => setText(event.detail.value ?? "")} />
        </IonToolbar>
      </IonHeader>
      <IonContent color="light">
        <div className="pm-page-body">
          <Footnote>{line.name}</Footnote>
          {line.candidates.length > 0 && debounced.length < 2 && (
            <>
              <SectionHeader level={3}>{t("purchaseEdit.candidates")}</SectionHeader>
              <IonList inset className="pm-after-header">{line.candidates.map((candidate) => row(candidate.productId, candidate.name))}</IonList>
            </>
          )}
          <div aria-live="polite">
            {search.isFetching && <Footnote>{t("purchaseEdit.searching")}</Footnote>}
            {search.isError && <Footnote tone="warning">{t("purchaseEdit.searchError")}</Footnote>}
            {search.isSuccess && results.length === 0 && <Footnote>{t("purchaseEdit.noResults")}</Footnote>}
          </div>
          {results.length > 0 && <IonList inset>{results.map((product) => row(product.id, product.name))}</IonList>}
          <IonList inset>
            <IonItem button detail={false} onClick={() => onChoose({ productLink: "new", productId: null, productName: null })}>
              <IonLabel color="primary">{t("purchaseEdit.createProduct")}</IonLabel>
            </IonItem>
            <IonItem button detail={false} lines="none" onClick={() => onChoose({ productLink: "auto", productId: null, productName: null })}>
              <IonLabel color="primary">{t("purchaseEdit.autoProduct")}</IonLabel>
            </IonItem>
          </IonList>
        </div>
      </IonContent>
    </IonModal>
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
