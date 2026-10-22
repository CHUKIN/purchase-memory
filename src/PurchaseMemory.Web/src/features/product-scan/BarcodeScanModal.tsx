import { IonButton, IonButtons, IonContent, IonHeader, IonModal, IonTitle, IonToolbar } from "@ionic/react";
import { Camera, Keyboard } from "lucide-react";
import { useId, useState } from "react";
import { useTranslation } from "react-i18next";
import { useHistoryLayer } from "../../design-system/history/historyLayers";
import { HelpLink } from "../help/HelpLink";
import { BarcodeCamera } from "./BarcodeCamera";
import { ManualBarcodeForm } from "./ManualBarcodeForm";
import type { BarcodeSource } from "./productMediaStore";
import "./productScan.css";

/**
 * A full-screen camera that reads one barcode and hands it back, for «add a
 * barcode to this product» (product card, purchase line). The shop scanner
 * with results is `ScanPage`.
 */
export function BarcodeScanModal({ isOpen, onClose, onCode }: {
  isOpen: boolean;
  onClose: () => void;
  onCode: (code: string, source: BarcodeSource) => void;
}) {
  const { t } = useTranslation();
  const titleId = useId();
  const [manual, setManual] = useState(false);
  useHistoryLayer(isOpen, onClose);

  const finish = (code: string, source: BarcodeSource) => {
    onCode(code, source);
    onClose();
  };

  return (
    <IonModal isOpen={isOpen} onDidDismiss={() => { setManual(false); onClose(); }} aria-labelledby={titleId}>
      <IonHeader>
        <IonToolbar>
          <IonButtons slot="start"><IonButton onClick={onClose}>{t("ui.cancel")}</IonButton></IonButtons>
          <IonTitle><h2 id={titleId} className="pm-title-heading">{t("productScan.scannerTitle")}</h2></IonTitle>
          <IonButtons slot="end">
            <HelpLink topic="product-scan" />
            <IonButton
              className="pm-icon-button"
              onClick={() => setManual((value) => !value)}
              aria-label={manual ? t("productScan.backToCamera") : t("productScan.manualEntry")}
            >
              {manual ? <Camera size={22} aria-hidden="true" /> : <Keyboard size={22} aria-hidden="true" />}
            </IonButton>
          </IonButtons>
        </IonToolbar>
      </IonHeader>
      <IonContent color={manual ? "light" : undefined} scrollY={manual} className={manual ? undefined : "pm-scan-content"}>
        {manual
          ? <div className="pm-page-body"><ManualBarcodeForm submitLabel={t("productScan.manualAdd")} onSubmit={(code) => finish(code, "manual")} /></div>
          : (
            <div className="pm-scan-stage">
              {isOpen && <BarcodeCamera onCode={(code) => finish(code, "scan")} onManual={() => setManual(true)} />}
            </div>
          )}
      </IonContent>
    </IonModal>
  );
}
