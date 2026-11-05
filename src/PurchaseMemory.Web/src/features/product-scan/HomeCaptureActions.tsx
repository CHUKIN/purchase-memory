import { Camera, ScanBarcode } from "lucide-react";
import { useTranslation } from "react-i18next";
import { useReturnState } from "../../app/backNavigation";
import { ButtonLink } from "../../design-system/components";
import "./productScan.css";

/**
 * The main actions at the top of Главная: the big receipt button, and
 * «Сканировать штрихкод» under it as a second, outlined one.
 */
export function HomeCaptureActions() {
  const { t } = useTranslation();
  const returnState = useReturnState();

  return (
    <div className="pm-primary-block">
      <ButtonLink to="/purchases/new" state={returnState} expand="block" shape="round" size="large">
        <Camera aria-hidden="true" size={22} />&nbsp;{t("common.captureReceipt")}
      </ButtonLink>
      <ButtonLink to="/scan" state={returnState} expand="block" shape="round" size="large" fill="outline">
        <ScanBarcode aria-hidden="true" size={22} />&nbsp;{t("productScan.homeScanButton")}
      </ButtonLink>
    </div>
  );
}
