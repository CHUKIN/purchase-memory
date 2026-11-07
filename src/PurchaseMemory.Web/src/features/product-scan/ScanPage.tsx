import { IonButton, IonButtons, IonContent, IonHeader, IonTitle, IonToolbar } from "@ionic/react";
import { Bookmark, Keyboard, RotateCw } from "lucide-react";
import { useCallback, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router";
import { useBackTarget, useReturnState } from "../../app/backNavigation";
import { BackButton, Notice, Sheet } from "../../design-system/components";
import { HelpLink } from "../help/HelpLink";
import { BarcodeCamera } from "./BarcodeCamera";
import { ManualBarcodeForm } from "./ManualBarcodeForm";
import { FoundResult, NotFoundResult, Searching } from "./ScanResults";
import { useBarcodeOwner, useRememberedBarcodes } from "./useProductMedia";
import "./productScan.css";

/**
 * «Сканировать» in the shop: the rear camera reads a barcode, and a sheet
 * over the still-running camera says whether this is a known product of the
 * household — when and where it was bought, the cheapest store, your rating,
 * «Больше не покупать» — or offers to link the code to one of your products.
 * The next scan is one tap away.
 */
export function ScanPage() {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage === "en" ? "en-GB" : "ru-RU";
  const navigate = useNavigate();
  const back = useBackTarget({ to: "/", labelKey: "common.home" });
  const returnState = useReturnState();
  const [code, setCode] = useState<string | null>(null);
  const [manualOpen, setManualOpen] = useState(false);
  /** Set after linking an unknown code, so the result turns into «found» without a rescan. */
  const [linkedProductId, setLinkedProductId] = useState<string | null>(null);
  const owner = useBarcodeOwner(code);
  const rememberedCount = useRememberedBarcodes().data?.length ?? 0;
  const productId = linkedProductId ?? owner.data ?? null;

  const showCode = useCallback((next: string) => {
    setLinkedProductId(null);
    setCode(next);
  }, []);
  const scanAgain = () => {
    setCode(null);
    setLinkedProductId(null);
  };
  const openProduct = (id: string) => {
    // Close the result sheet first, so no overlay outlives the screen.
    setCode(null);
    navigate(`/products/${id}`, { state: returnState });
  };
  // The remembered codes are a section of «Ревью»: link, create or forget them there.
  const openRemembered = () => navigate("/needs-attention?open=barcodes", { state: returnState });
  const fromManual = (next: string) => {
    setManualOpen(false);
    showCode(next);
  };

  const searching = owner.isPending && !linkedProductId;
  // Without the server's answer the code could be either; saying «new» would mislead.
  const failed = owner.isError && !linkedProductId;
  const title = code === null ? t("productScan.scanTitle")
    : searching ? t("productScan.resultSearching")
    : failed ? t("productScan.resultFailed")
    : productId ? t("productScan.resultFound")
    : t("productScan.resultNew");

  const result = code === null ? null
    : searching ? <Searching code={code} />
    : failed
      ? (
        <>
          <Notice tone="danger" role="alert"><p>{t("productScan.lookupFailed")}</p></Notice>
          <div className="pm-scan-buttons">
            <IonButton expand="block" shape="round" onClick={() => void owner.refetch()}>
              <RotateCw size={20} aria-hidden="true" />&nbsp;{t("productScan.retry")}
            </IonButton>
            <IonButton expand="block" shape="round" fill="clear" onClick={scanAgain}>{t("productScan.scanAgain")}</IonButton>
          </div>
        </>
      )
    : productId
      ? <FoundResult productId={productId} code={code} locale={locale} onOpenProduct={() => openProduct(productId)} onScanAgain={scanAgain} />
      : <NotFoundResult code={code} locale={locale} onLinked={setLinkedProductId} onCreated={openProduct} onScanAgain={scanAgain} />;

  return (
    <div className="ion-page">
      <IonHeader>
        <IonToolbar>
          <IonButtons slot="start">
            <BackButton target={back} />
          </IonButtons>
          <IonTitle><h1 className="pm-title-heading">{t("productScan.scanTitle")}</h1></IonTitle>
          <IonButtons slot="end">
            <HelpLink topic="product-scan" />
            <IonButton className="pm-icon-button" onClick={() => setManualOpen(true)} aria-label={t("productScan.manualEntry")}>
              <Keyboard size={22} aria-hidden="true" />
            </IonButton>
          </IonButtons>
        </IonToolbar>
      </IonHeader>
      <IonContent scrollY={false} className="pm-scan-content">
        <div className="pm-scan-stage">
          <BarcodeCamera active paused={code !== null || manualOpen} onCode={showCode} onManual={() => setManualOpen(true)} />
          <div className="pm-scan-bottom">
            <IonButton fill="clear" color="light" onClick={() => setManualOpen(true)}>
              <Keyboard size={20} aria-hidden="true" />&nbsp;{t("productScan.manualEntry")}
            </IonButton>
            {rememberedCount > 0 && (
              <IonButton fill="clear" color="light" onClick={openRemembered}>
                <Bookmark size={20} aria-hidden="true" />&nbsp;{t("productScan.rememberedList", { count: rememberedCount })}
              </IonButton>
            )}
          </div>
        </div>
      </IonContent>

      {/* The result slides up over the camera. */}
      <Sheet
        isOpen={code !== null}
        onClose={scanAgain}
        title={title}
        end={<IonButton strong onClick={scanAgain}>{t("ui.close")}</IonButton>}
        initialBreakpoint={0.7}
      >
        {result}
      </Sheet>

      <Sheet isOpen={manualOpen} onClose={() => setManualOpen(false)} title={t("productScan.manualTitle")} initialBreakpoint={0.6}>
        <ManualBarcodeForm submitLabel={t("productScan.manualFind")} onSubmit={fromManual} />
      </Sheet>
    </div>
  );
}
