import { IonButton, IonItem, IonLabel, IonList } from "@ionic/react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import type { ProductSummary } from "../../api/types";
import { Notice, Sheet } from "../../design-system/components";
import { CatalogSearchModal } from "../products/CatalogSearchModal";
import { NewProductForm } from "../product-scan/NewProductForm";
import type { CreateForBarcodeResult, RememberedBarcode } from "../product-scan/useProductMedia";
import { useToast } from "../../app/toasts";

export interface BarcodeItemsProps {
  codes: RememberedBarcode[];
  /** Links the code to the chosen product; resolves `false` when another product already has it. */
  onLink: (code: string, product: ProductSummary) => Promise<boolean>;
  onForget: (code: string) => Promise<void>;
  /** Creates a product for the code (name and brand from the form); `offName` is the database's name, when known. */
  onCreate?: (code: string, name: string, brand: string, offName: string | null) => Promise<CreateForBarcodeResult>;
  /** Opens the card of the product just created. */
  onOpenProduct?: (productId: string) => void;
}

/**
 * «Штрихкоды»: codes scanned in the shop with «Запомнить на потом». Each one
 * is linked to a product found by name («Привязать к товару», a full-screen
 * search that is one back step), turned into a new product («Создать товар»,
 * the name from Open Food Facts is prefilled when it is known) or forgotten. The codes are the household's:
 * both members see and settle the same list.
 */
export function BarcodeItems({ codes, onLink, onForget, onCreate, onOpenProduct }: BarcodeItemsProps) {
  const { t, i18n } = useTranslation();
  const showToast = useToast();
  const [linking, setLinking] = useState<RememberedBarcode | null>(null);
  const [failed, setFailed] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [creating, setCreating] = useState<RememberedBarcode | null>(null);
  const [createError, setCreateError] = useState<string | null>(null);
  const day = new Intl.DateTimeFormat(i18n.language, { day: "numeric", month: "long" });

  const run = async (code: string, action: () => Promise<void>) => {
    setBusy(true);
    setFailed(null);
    try {
      await action();
    } catch {
      setFailed(code);
    } finally {
      setBusy(false);
    }
  };

  const choose = (product: ProductSummary) => {
    const code = linking;
    setLinking(null);
    if (!code) return;
    void run(code.code, async () => {
      const linked = await onLink(code.code, product);
      showToast({
        id: `barcode-${code.code}-${product.id}-${Date.now()}`,
        message: linked ? t("reviewQueue.barcodes.linked", { name: product.name }) : t("reviewQueue.barcodes.taken"),
        tone: linked ? "success" : "danger"
      });
    });
  };

  const create = async (name: string, brand: string) => {
    if (!creating || !onCreate) return;
    const entry = creating;
    setBusy(true);
    setCreateError(null);
    try {
      const result = await onCreate(entry.code, name, brand, entry.off?.name ?? null);
      if (result.ok) {
        setCreating(null);
        onOpenProduct?.(result.productId);
        return;
      }
      setCreateError(
        result.reason === "invalid" ? result.message ?? t("productScan.createFailed")
          : result.reason === "taken" ? t("productScan.linkTaken")
          : result.reason === "notLinked" ? t("productScan.createdNotLinked")
          : t("productScan.createFailed")
      );
    } catch {
      setCreateError(t("productScan.createFailed"));
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      {codes.map((entry) => {
        const offName = entry.off ? [entry.off.brand, entry.off.name].filter(Boolean).join(" · ") : "";
        return (
          <article key={entry.code} aria-label={entry.code}>
            <IonList inset className="pm-after-header pm-queue-item">
              <IonItem>
                <IonLabel className="pm-wrap">
                  <h3 className="pm-barcode-code">{entry.code}</h3>
                  {offName && <p>{offName}</p>}
                  <p>{t("reviewQueue.barcodes.remembered", { date: day.format(new Date(entry.rememberedAt)) })}</p>
                </IonLabel>
              </IonItem>
              <IonItem button detail disabled={busy} onClick={() => setLinking(entry)} aria-label={t("reviewQueue.barcodes.linkFor", { code: entry.code })}>
                <IonLabel color="primary">{t("reviewQueue.barcodes.link")}</IonLabel>
              </IonItem>
              {onCreate && (
                <IonItem button detail disabled={busy} onClick={() => { setCreateError(null); setCreating(entry); }} aria-label={t("reviewQueue.barcodes.createFor", { code: entry.code })}>
                  <IonLabel color="primary">{t("reviewQueue.barcodes.create")}</IonLabel>
                </IonItem>
              )}
              <IonItem button detail={false} disabled={busy} onClick={() => void run(entry.code, () => onForget(entry.code))} aria-label={t("reviewQueue.barcodes.forgetFor", { code: entry.code })}>
                <IonLabel color="primary">{t("reviewQueue.barcodes.forget")}</IonLabel>
              </IonItem>
              {failed === entry.code && (
                <IonItem lines="none"><IonLabel className="pm-wrap pm-danger-text" role="alert">{t("productScan.saveFailed")}</IonLabel></IonItem>
              )}
            </IonList>
          </article>
        );
      })}
      <CatalogSearchModal
        isOpen={linking !== null}
        onClose={() => setLinking(null)}
        title={t("reviewQueue.barcodes.linkTitle")}
        subject={linking ? [linking.code, linking.off?.name].filter(Boolean).join(" · ") : ""}
        onChoose={choose}
        chooseLabel={(name) => t("reviewQueue.barcodes.chooseFor", { name })}
      />
      <Sheet
        isOpen={creating !== null}
        onClose={() => setCreating(null)}
        title={t("reviewQueue.barcodes.createTitle")}
        end={<IonButton strong onClick={() => setCreating(null)}>{t("ui.close")}</IonButton>}
        initialBreakpoint={0.7}
      >
        {creating && (
          <>
            <p className="pm-home-intro">{[creating.code, creating.off?.name && t("reviewQueue.barcodes.offName", { name: creating.off.name })].filter(Boolean).join(" · ")}</p>
            {createError && <Notice tone="danger" role="alert"><p>{createError}</p></Notice>}
            <NewProductForm
              key={creating.code}
              initialName={creating.off?.name ?? ""}
              initialBrand={creating.off?.brand ?? ""}
              busy={busy}
              onCreate={(name, brand) => void create(name, brand)}
              onCancel={() => setCreating(null)}
            />
          </>
        )}
      </Sheet>
    </>
  );
}
