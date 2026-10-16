import { Camera, ScanBarcode } from "lucide-react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { useReturnState } from "../../app/backNavigation";
import { ButtonLink } from "../../design-system/components";
import { useScanVariant } from "./scanVariant";
import "./productScan.css";

/**
 * The main actions at the top of Главная. Variant A: two equal tiles, «Чек»
 * and «Сканировать», since scanning in the shop matters as much as the
 * receipt. Variant B: the big receipt button stays, «Сканировать штрихкод»
 * sits under it as a second, outlined one.
 */
export function HomeCaptureActions() {
  const { t } = useTranslation();
  const returnState = useReturnState();
  const variant = useScanVariant();

  if (variant === "a") {
    return (
      <div className="pm-scan-tiles">
        <Link to="/purchases/new" state={returnState} className="pm-scan-tile pm-scan-tile-main" aria-label={t("common.captureReceipt")}>
          <Camera size={30} aria-hidden="true" />{t("productScan.homeTileReceipt")}
        </Link>
        <Link to="/scan" state={returnState} className="pm-scan-tile pm-scan-tile-second" aria-label={t("productScan.homeScanButton")}>
          <ScanBarcode size={30} aria-hidden="true" />{t("productScan.homeTileScan")}
        </Link>
      </div>
    );
  }

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
