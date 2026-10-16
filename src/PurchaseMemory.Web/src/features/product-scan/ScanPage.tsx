import { IonButton, IonButtons, IonContent, IonHeader, IonTitle, IonToolbar } from "@ionic/react";
import { Keyboard } from "lucide-react";
import { useCallback, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router";
import { useBackTarget, useReturnState } from "../../app/backNavigation";
import { BackButton, Sheet } from "../../design-system/components";
import { HelpLink } from "../help/HelpLink";
import { BarcodeCamera } from "./BarcodeCamera";
import { ManualBarcodeForm } from "./ManualBarcodeForm";
import { FoundResult, NotFoundResult, Searching } from "./ScanResults";
import { useScanVariant } from "./scanVariant";
import { useBarcodeOwner } from "./useProductMedia";
import "./productScan.css";

/** How long a just-closed code is ignored, while the camera restarts with the pack still in view. */
const closedRepeatMs = 3000;

/**
 * «Сканировать» in the shop: the rear camera reads a barcode, and the screen
 * says whether this is a known product — when and where it was bought, the
 * cheapest store, your rating, «Больше не покупать» — or offers to link the
 * code to one of your products.
 *
 * Variant A shows the result as a full screen (the camera stops); variant B
 * as a sheet over the still-running camera, so the next scan is one tap away.
 */
export function ScanPage() {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage === "en" ? "en-GB" : "ru-RU";
  const variant = useScanVariant();
  const navigate = useNavigate();
  const back = useBackTarget({ to: "/", labelKey: "common.home" });
  const returnState = useReturnState();
  const [code, setCode] = useState<string | null>(null);
  const [manualOpen, setManualOpen] = useState(false);
  /** Set after linking an unknown code, so the result turns into «found» without a rescan. */
  const [linkedProductId, setLinkedProductId] = useState<string | null>(null);
  const owner = useBarcodeOwner(code);
  const productId = linkedProductId ?? owner.data ?? null;

  /**
   * The code whose result was just closed. Variant A stops the camera under a
   * result, so a restarted camera has no memory of the pack still in front of
   * it; this keeps that pack from reopening the same result at once.
   */
  const closedRef = useRef<{ code: string; at: number } | null>(null);

  const showCode = useCallback((next: string) => {
    closedRef.current = null;
    setLinkedProductId(null);
    setCode(next);
  }, []);
  const onCode = useCallback((next: string) => {
    const closed = closedRef.current;
    if (closed && closed.code === next && Date.now() - closed.at < closedRepeatMs) return;
    showCode(next);
  }, [showCode]);
  const scanAgain = () => {
    if (code !== null) closedRef.current = { code, at: Date.now() };
    setCode(null);
    setLinkedProductId(null);
  };
  const openProduct = (id: string) => {
    // Close the result sheet first, so no overlay outlives the screen.
    setCode(null);
    navigate(`/products/${id}`, { state: returnState });
  };
  const fromManual = (next: string) => {
    setManualOpen(false);
    // Typed on purpose: shown even if it is the code just closed.
    showCode(next);
  };

  const title = code === null ? t("productScan.scanTitle")
    : owner.isPending && !linkedProductId ? t("productScan.resultSearching")
    : productId ? t("productScan.resultFound")
    : t("productScan.resultNew");

  const result = code === null ? null
    : owner.isPending && !linkedProductId ? <Searching code={code} />
    : productId
      ? <FoundResult productId={productId} code={code} locale={locale} onOpenProduct={() => openProduct(productId)} onScanAgain={scanAgain} />
      : <NotFoundResult code={code} locale={locale} onLinked={setLinkedProductId} onScanAgain={scanAgain} />;

  const full = variant === "a" && code !== null;

  return (
    <div className="ion-page">
      <IonHeader>
        <IonToolbar>
          <IonButtons slot="start">
            {full ? <IonButton onClick={scanAgain}>{t("productScan.scanAgainShort")}</IonButton> : <BackButton target={back} />}
          </IonButtons>
          <IonTitle><h1 className="pm-title-heading">{full ? title : t("productScan.scanTitle")}</h1></IonTitle>
          <IonButtons slot="end">
            <HelpLink topic="product-scan" />
            {full
              ? <IonButton strong onClick={() => navigate(back.to)}>{t("ui.done")}</IonButton>
              : (
                <IonButton className="pm-icon-button" onClick={() => setManualOpen(true)} aria-label={t("productScan.manualEntry")}>
                  <Keyboard size={22} aria-hidden="true" />
                </IonButton>
              )}
          </IonButtons>
        </IonToolbar>
      </IonHeader>
      <IonContent color={full ? "light" : undefined} scrollY={full} className={full ? undefined : "pm-scan-content"}>
        {full
          ? <div className="pm-page-body">{result}</div>
          : (
            <div className="pm-scan-stage">
              <BarcodeCamera active paused={code !== null || manualOpen} onCode={onCode} onManual={() => setManualOpen(true)} />
              <div className="pm-scan-bottom">
                <IonButton fill="clear" color="light" onClick={() => setManualOpen(true)}>
                  <Keyboard size={20} aria-hidden="true" />&nbsp;{t("productScan.manualEntry")}
                </IonButton>
              </div>
            </div>
          )}
      </IonContent>

      {/* Variant B: the result slides up over the camera. */}
      {variant === "b" && (
        <Sheet
          isOpen={code !== null}
          onClose={scanAgain}
          title={title}
          end={<IonButton strong onClick={scanAgain}>{t("ui.close")}</IonButton>}
          initialBreakpoint={0.7}
        >
          {result}
        </Sheet>
      )}

      <Sheet isOpen={manualOpen} onClose={() => setManualOpen(false)} title={t("productScan.manualTitle")} initialBreakpoint={0.6}>
        <ManualBarcodeForm submitLabel={t("productScan.manualFind")} onSubmit={fromManual} />
      </Sheet>
    </div>
  );
}
