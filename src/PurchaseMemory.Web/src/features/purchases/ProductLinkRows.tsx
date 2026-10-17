import { IonItem, IonLabel, IonList } from "@ionic/react";
import { useQuery } from "@tanstack/react-query";
import { CircleAlert, Package, PackagePlus, PackageX } from "lucide-react";
import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import type { ProductLinkChoice } from "../../api/types";
import type { TFunction } from "i18next";
import { getProduct } from "../../api/client";
import { SectionHeader } from "../../design-system/components";
import { CatalogSearchModal } from "../products/CatalogSearchModal";

/** The status of a line's catalog product, as one short sentence. */
export function productStatusText(t: TFunction, line: Pick<LinkableLine, "productLink" | "productName" | "name" | "productNeedsReview">) {
  if (line.productLink === "existing" && line.productName) {
    const linked = t("purchaseEdit.productLinked", { name: line.productName });
    return line.productNeedsReview ? `${linked}. ${t("purchaseEdit.productNeedsReview")}` : linked;
  }
  return line.productLink === "new"
    ? t("purchaseEdit.productNew", { name: line.name.trim() || "…" })
    : t("purchaseEdit.productAuto");
}

const sameName = (a: string, b: string) => a.trim().toLocaleLowerCase() === b.trim().toLocaleLowerCase();

/**
 * A line's catalog product in a list row, icon first: the product's name when
 * it differs from the line's, «проверить» when the product carries that
 * flag, «Новый товар» for a new one, «Без товара» for a line left unlinked
 * (only very old data), and nothing more when it is obvious. The full
 * sentence (`text`, e.g. «Связано с «…»») is always there for screen readers.
 */
export function ProductLinkNote({ text, lineName, productName, flagged = false, isNew = false, unlinked = false }: {
  text: string;
  lineName: string;
  productName: string | null;
  /** The linked product carries the «проверить» flag. */
  flagged?: boolean;
  isNew?: boolean;
  unlinked?: boolean;
}) {
  const { t } = useTranslation();
  let visible: ReactNode = null;
  if (isNew) visible = <><PackagePlus size={13} className="pm-inline-icon" />{t("purchaseEdit.productNewShort")}</>;
  else if (unlinked) visible = <><PackageX size={13} className="pm-inline-icon" />{t("purchaseEdit.productUnlinkedShort")}</>;
  else if (productName && !sameName(productName, lineName)) visible = <><Package size={13} className="pm-inline-icon" />{productName}</>;
  return (
    <span className="pm-line-detail pm-link-note" data-warn={flagged}>
      {visible && <span aria-hidden="true">{visible}</span>}
      {flagged && <span aria-hidden="true"><CircleAlert size={13} className="pm-inline-icon" />{t("purchaseEdit.productNeedsReviewShort")}</span>}
      <span className="visually-hidden">{text}</span>
    </span>
  );
}

export interface LinkableLine {
  name: string;
  productLink: ProductLinkChoice;
  productId: string | null;
  productName: string | null;
  /** The linked product carries the «проверить» flag. */
  productNeedsReview?: boolean;
}

export type LinkChoice = Pick<LinkableLine, "productLink" | "productId" | "productName" | "productNeedsReview">;

/**
 * «Товар в каталоге» inside a purchase line's sheet: what the line is linked
 * to (with «проверить» when the product carries the flag), and «Изменить
 * товар», which opens a full-screen picker: a search over the catalog, a new
 * product, or «decide automatically after saving». Used by the purchase
 * editor and by the swipe review's «correct this line».
 */
export function ProductLinkRows({ line, number, pickerOpen, onPickerOpenChange, onChoose, onOpenProduct }: {
  line: LinkableLine;
  number: number;
  pickerOpen: boolean;
  onPickerOpenChange: (open: boolean) => void;
  onChoose: (choice: LinkChoice) => void;
  /**
   * Opens the linked product's card. Given only when leaving the screen loses
   * nothing (no unsaved edits); shown only for a line linked to a product.
   */
  onOpenProduct?: () => void;
}) {
  const { t } = useTranslation();
  const flagged = line.productLink === "existing" && Boolean(line.productNeedsReview);
  return (
    <>
      <SectionHeader level={3}>{t("purchaseEdit.product")}</SectionHeader>
      <IonList inset className="pm-after-header">
        <IonItem>
          {flagged && <CircleAlert slot="start" size={20} className="pm-row-icon pm-warning-icon" aria-hidden="true" />}
          <IonLabel className={["pm-wrap", flagged ? "pm-warn-text" : ""].filter(Boolean).join(" ")}>{productStatusText(t, line)}</IonLabel>
        </IonItem>
        {onOpenProduct && line.productLink === "existing" && line.productId && (
          <IonItem button detail onClick={onOpenProduct} aria-label={t("purchaseEdit.openProduct", { name: line.productName ?? line.name })}>
            <IonLabel color="primary">{t("purchaseEdit.openProductShort")}</IonLabel>
          </IonItem>
        )}
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
  // A flagged product keeps the matcher's candidates; they are the first guesses here too.
  const flaggedId = line.productLink === "existing" && line.productNeedsReview ? line.productId : null;
  const flagged = useQuery({
    queryKey: ["products", "detail", flaggedId ?? ""],
    queryFn: () => getProduct(flaggedId ?? ""),
    enabled: isOpen && flaggedId !== null
  });
  const candidates = flaggedId ? flagged.data?.reviewCandidates ?? [] : [];
  return (
    <CatalogSearchModal
      isOpen={isOpen}
      onClose={onClose}
      title={t("purchaseEdit.chooseProduct", { number })}
      subject={line.name}
      selectedProductId={line.productLink === "existing" ? line.productId : null}
      chooseLabel={(name) => t("purchaseEdit.useProduct", { name })}
      onChoose={(product) => onChoose({
        productLink: "existing",
        productId: product.id,
        productName: product.name,
        productNeedsReview: Boolean(product.needsReview)
      })}
      before={candidates.length > 0 && (
        <>
          <SectionHeader level={3}>{t("purchaseEdit.candidates")}</SectionHeader>
          <IonList inset className="pm-after-header">
            {candidates.map((candidate) => (
              <IonItem
                key={candidate.productId}
                button
                detail={false}
                onClick={() => onChoose({ productLink: "existing", productId: candidate.productId, productName: candidate.name, productNeedsReview: false })}
                aria-label={t("purchaseEdit.useProduct", { name: candidate.name })}
              >
                <IonLabel className="pm-wrap">{candidate.name}</IonLabel>
              </IonItem>
            ))}
          </IonList>
        </>
      )}
      after={(
        <IonList inset>
          {/* An ordinary product named after the line, never a flagged one. */}
          <IonItem button detail={false} onClick={() => onChoose({ productLink: "new", productId: null, productName: null, productNeedsReview: false })}>
            <IonLabel color="primary">{t("purchaseEdit.createProduct")}</IonLabel>
          </IonItem>
          <IonItem button detail={false} lines="none" onClick={() => onChoose({ productLink: "auto", productId: null, productName: null, productNeedsReview: false })}>
            <IonLabel color="primary">{t("purchaseEdit.autoProduct")}</IonLabel>
          </IonItem>
        </IonList>
      )}
    />
  );
}
