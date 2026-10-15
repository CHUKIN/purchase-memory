import { IonItem, IonLabel, IonList } from "@ionic/react";
import { Camera, ImagePlus, ScanBarcode } from "lucide-react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Footnote, Notice, SectionHeader } from "../../design-system/components";
import { formatBarcode } from "./barcode";
import { BarcodeScanModal } from "./BarcodeScanModal";
import { PhotoDecodeError } from "./compressImage";
import type { BarcodeSource } from "./productMediaStore";
import { ProductThumb, usePhotoPicker } from "./ProductPhoto";
import { useProductMedia, useProductMediaActions } from "./useProductMedia";
import "./productScan.css";

/**
 * «Товар» in a purchase line's sheet: with the line linked to a product, a
 * photo or a barcode taken here goes straight to that product's card. An
 * unlinked line first needs its product.
 */
export function LineProductMedia({ productId, productName }: { productId: string | null | undefined; productName: string | null | undefined }) {
  const { t } = useTranslation();
  return (
    <section aria-labelledby="line-product-media">
      <SectionHeader id="line-product-media">{t("productScan.lineProductTitle")}</SectionHeader>
      {productId && productName
        ? <LinkedLineMedia productId={productId} productName={productName} />
        : <Footnote tone="warning">{t("productScan.lineNotLinked")}</Footnote>}
    </section>
  );
}

function LinkedLineMedia({ productId, productName }: { productId: string; productName: string }) {
  const { t } = useTranslation();
  const media = useProductMedia(productId);
  const actions = useProductMediaActions(productId);
  const [busy, setBusy] = useState(false);
  const [scanning, setScanning] = useState(false);
  const [message, setMessage] = useState<{ tone: "success" | "danger"; text: string } | null>(null);

  async function run(action: () => Promise<string>) {
    setBusy(true);
    setMessage(null);
    try {
      setMessage({ tone: "success", text: await action() });
    } catch (caught) {
      const text = caught instanceof PhotoDecodeError ? t("productScan.photoFailed")
        : caught instanceof BarcodeTakenError ? t("productScan.barcodeTaken", { code: formatBarcode(caught.code) })
        : t("productScan.saveFailed");
      setMessage({ tone: "danger", text });
    } finally {
      setBusy(false);
    }
  }

  const picker = usePhotoPicker(
    (file) => void run(async () => { await actions.addPhoto(file); return t("productScan.photoAdded"); }),
    { camera: t("productScan.lineTakePhoto"), library: t("productScan.linePickPhoto") }
  );

  const addBarcode = (code: string, source: BarcodeSource) => void run(async () => {
    const result = await actions.addBarcode(code, source);
    if (!result.ok) throw new BarcodeTakenError(code);
    return t("productScan.barcodeAdded", { code: formatBarcode(code) });
  });

  const photos = media.data?.photos.length ?? 0;
  const barcodes = media.data?.barcodes.length ?? 0;

  return (
    <>
      {picker.inputs}
      <IonList inset className="pm-after-header">
        <IonItem lines="none">
          {media.data?.mainPhotoId
            ? <ProductThumb productId={productId} />
            : <Camera slot="start" size={22} className="pm-row-icon pm-muted-icon" aria-hidden="true" />}
          <IonLabel className="pm-wrap">
            <h3>{productName}</h3>
            <p>{t("productScan.lineMediaSummary", { photos, barcodes })}</p>
          </IonLabel>
        </IonItem>
      </IonList>
      <IonList inset>
        <IonItem button detail={false} disabled={busy} onClick={picker.openCamera}>
          <Camera slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
          <IonLabel color="primary">{t("productScan.lineTakePhoto")}</IonLabel>
        </IonItem>
        <IonItem button detail={false} disabled={busy} onClick={picker.openLibrary}>
          <ImagePlus slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
          <IonLabel color="primary">{t("productScan.linePickPhoto")}</IonLabel>
        </IonItem>
        <IonItem button detail={false} lines="none" disabled={busy} onClick={() => setScanning(true)}>
          <ScanBarcode slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
          <IonLabel color="primary">{t("productScan.lineScan")}</IonLabel>
        </IonItem>
      </IonList>
      {message
        ? <Notice tone={message.tone} role={message.tone === "danger" ? "alert" : "status"}><p>{message.text}</p></Notice>
        : <Footnote>{t("productScan.lineAttachNote")}</Footnote>}
      <BarcodeScanModal isOpen={scanning} onClose={() => setScanning(false)} onCode={addBarcode} />
    </>
  );
}

class BarcodeTakenError extends Error {
  readonly code: string;
  constructor(code: string) {
    super("The barcode belongs to another product.");
    this.code = code;
  }
}
