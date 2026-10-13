import { IonButton, IonButtons, IonContent, IonHeader, IonItem, IonLabel, IonList, IonModal, IonSearchbar, IonTitle, IonToolbar } from "@ionic/react";
import { useQuery } from "@tanstack/react-query";
import { Check } from "lucide-react";
import { useEffect, useId, useMemo, useState } from "react";
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
