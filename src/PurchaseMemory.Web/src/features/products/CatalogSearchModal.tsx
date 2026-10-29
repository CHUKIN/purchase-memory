import { IonButton, IonButtons, IonContent, IonHeader, IonItem, IonLabel, IonList, IonModal, IonSearchbar, IonTitle, IonToolbar } from "@ionic/react";
import { useQuery } from "@tanstack/react-query";
import { Check } from "lucide-react";
import { useId, useMemo, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { getProductCatalog } from "../../api/client";
import type { ProductSummary } from "../../api/types";
import { Footnote } from "../../design-system/components";
import { useHistoryLayer } from "../../design-system/history/historyLayers";
import { useDebounced } from "../filters/useDebounced";
import { displayName, russianName } from "./productNameText";
import { ReviewFlagChip } from "./ReviewFlagChip";

export interface CatalogSearchModalProps {
  isOpen: boolean;
  onClose: () => void;
  title: string;
  /** What is being decided about, e.g. the receipt line or the product to merge. */
  subject: string;
  /** Left out of the results (a product cannot be merged into itself). */
  excludeProductId?: string;
  /** Marked with ✓ in the results. */
  selectedProductId?: string | null;
  onChoose: (product: ProductSummary) => void;
  /** Accessible name of a result row. */
  chooseLabel: (name: string) => string;
  /** Rows above the results while nothing is typed (e.g. similar products). */
  before?: ReactNode;
  /** Rows under the results (e.g. «Создать новый товар»). */
  after?: ReactNode;
}

/**
 * A full-screen search over the catalog, the way a person finds a product by
 * the wording on the till. Shared by the purchase editor's product picker and
 * «Объединить с…» on the product card.
 */
export function CatalogSearchModal({
  isOpen,
  onClose,
  title,
  subject,
  excludeProductId,
  selectedProductId = null,
  onChoose,
  chooseLabel,
  before,
  after
}: CatalogSearchModalProps) {
  const { t } = useTranslation();
  const titleId = useId();
  const [text, setText] = useState("");
  useHistoryLayer(isOpen, onClose);
  const debounced = useDebounced(text.trim());
  const search = useQuery({
    queryKey: ["products", "catalog", "picker", debounced, excludeProductId ?? ""],
    queryFn: () => getProductCatalog({ query: debounced, excludeProductId, pageSize: 8 }),
    enabled: isOpen && debounced.length >= 2
  });
  const results = useMemo(() => search.data?.items ?? [], [search.data]);

  return (
    <IonModal isOpen={isOpen} onDidDismiss={() => { setText(""); onClose(); }} aria-labelledby={titleId}>
      <IonHeader>
        <IonToolbar>
          <IonButtons slot="start"><IonButton onClick={onClose}>{t("ui.cancel")}</IonButton></IonButtons>
          <IonTitle><h2 id={titleId} className="pm-title-heading">{title}</h2></IonTitle>
        </IonToolbar>
        <IonToolbar>
          <IonSearchbar value={text} placeholder={t("purchaseEdit.searchPlaceholder")} aria-label={t("purchaseEdit.searchProduct")} onIonInput={(event) => setText(event.detail.value ?? "")} />
        </IonToolbar>
      </IonHeader>
      <IonContent color="light">
        <div className="pm-page-body">
          <Footnote>{subject}</Footnote>
          {debounced.length < 2 && before}
          <div aria-live="polite">
            {search.isFetching && <Footnote>{t("purchaseEdit.searching")}</Footnote>}
            {search.isError && <Footnote tone="warning">{t("purchaseEdit.searchError")}</Footnote>}
            {search.isSuccess && results.length === 0 && <Footnote>{t("purchaseEdit.noResults")}</Footnote>}
          </div>
          {results.length > 0 && (
            <IonList inset>
              {results.map((product) => {
                const selected = product.id === selectedProductId;
                return (
                  <IonItem key={product.id} button detail={false} onClick={() => onChoose(product)} aria-label={chooseLabel(displayName(product))} aria-current={selected || undefined}>
                    <IonLabel className="pm-wrap">
                      <h3>{displayName(product)}</h3>
                      <p>{[russianName(product), product.brand, t("products.purchaseCount", { count: product.purchaseCount })].filter(Boolean).join(" · ")}</p>
                      {product.needsReview && <div className="pm-chip-row"><ReviewFlagChip /></div>}
                    </IonLabel>
                    {selected && <Check slot="end" size={20} className="pm-check" aria-hidden="true" />}
                  </IonItem>
                );
              })}
            </IonList>
          )}
          {after}
        </div>
      </IonContent>
    </IonModal>
  );
}
