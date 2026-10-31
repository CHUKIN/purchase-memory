import type { TFunction } from "i18next";
import { IonActionSheet, IonButton, IonItem, IonLabel, IonList, IonToggle } from "@ionic/react";
import { Ban, Camera, ImagePlus, Plus, ScanBarcode, ScanText, Trash2 } from "lucide-react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useReturnState } from "../../app/backNavigation";
import { useToast } from "../../app/toasts";
import { ButtonLink, EmptyState, Footnote, Notice, SectionHeader } from "../../design-system/components";
import { formatBarcode } from "./barcode";
import { BarcodeScanModal } from "./BarcodeScanModal";
import { PhotoDecodeError } from "./compressImage";
import type { AvoidMark, BarcodeSource, ProductBarcode, ProductMedia } from "./productMediaStore";
import { ProductPhotoImage, usePhotoPicker } from "./ProductPhoto";
import { useScanVariant } from "./scanVariant";
import { useNameFromPhoto } from "./useNameFromPhoto";
import { useProductMedia, useProductMediaActions } from "./useProductMedia";
import "./productScan.css";

export interface ProductMediaSectionProps {
  productId: string;
  productName: string;
  /** «4 покупки»: the line under the title, which variant B puts beside the photo. */
  intro: string;
  locale: string;
}

/**
 * Photos, barcodes and «Больше не покупать» on the product card, in the look
 * chosen in Настройки (variant A: big gallery; B: compact strip and chips).
 * Kept on this device for now, see productMediaStore.ts.
 */
export function ProductMediaSection({ productId, productName, intro, locale }: ProductMediaSectionProps) {
  const { t } = useTranslation();
  const variant = useScanVariant();
  const showToast = useToast();
  const mediaQuery = useProductMedia(productId);
  const actions = useProductMediaActions(productId);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<{ text: string; otherProductId?: string } | null>(null);
  const [scanning, setScanning] = useState(false);
  const [confirmPhoto, setConfirmPhoto] = useState<string | null>(null);
  const [confirmCode, setConfirmCode] = useState<string | null>(null);
  const naming = useNameFromPhoto(productId);
  const media: ProductMedia = mediaQuery.data ?? { productId, photos: [], mainPhotoId: null, barcodes: [], avoid: null };

  async function run(action: () => Promise<unknown>) {
    setBusy(true);
    setError(null);
    try {
      await action();
    } catch (caught) {
      setError({ text: caught instanceof PhotoDecodeError ? t("productScan.photoFailed") : t("productScan.saveFailed") });
    } finally {
      setBusy(false);
    }
  }

  const picker = usePhotoPicker(
    // The first photo of a product is usually the package front: its name is read
    // right away (a suggested name is replaced with «Вернуть», a person's name only
    // offered). Later photos (back, sides) never rename; «Название по фото» does it on demand.
    (file) => void run(async () => {
      const isFirstPhoto = media.photos.length === 0;
      await actions.addPhoto(file);
      if (isFirstPhoto) void naming.start(file, { automatic: true });
    }),
    { camera: t("productScan.takePhoto"), library: t("productScan.pickPhoto") }
  );

  const addBarcode = (code: string, source: BarcodeSource) => void run(async () => {
    const result = await actions.addBarcode(code, source);
    if (result.ok) showToast({ id: `barcode-${productId}-${code}-${Date.now()}`, message: t("productScan.barcodeAdded", { code: formatBarcode(code) }), tone: "success" });
    else setError({ text: t("productScan.barcodeTaken", { code: formatBarcode(code) }), otherProductId: result.productId });
  });

  const photoProps: PhotoBlockProps = {
    media,
    productName,
    busy,
    onCamera: picker.openCamera,
    onLibrary: picker.openLibrary,
    onMakeMain: (photoId) => void run(() => actions.setMainPhoto(photoId)),
    onDelete: setConfirmPhoto
  };
  const nameFromMainPhoto = async () => {
    const photo = media.mainPhotoId ? await actions.readPhoto(media.mainPhotoId) : null;
    if (photo) await naming.start(photo);
  };
  const nameFromPhotoButton = media.photos.length > 0 && (
    <div className="pm-media-actions">
      <IonButton fill="clear" size="small" disabled={busy || naming.busy} onClick={() => void nameFromMainPhoto()}>
        <ScanText size={18} aria-hidden="true" />{t("products.names.fromPhoto")}
      </IonButton>
    </div>
  );
  const barcodeProps: BarcodeBlockProps = {
    barcodes: media.barcodes,
    busy,
    onScan: () => setScanning(true),
    onDelete: setConfirmCode
  };

  return (
    <section aria-label={t("productScan.mediaLabel")} className={busy ? "pm-media-busy" : undefined} aria-busy={busy}>
      {picker.inputs}
      {media.avoid && <AvoidBanner avoid={media.avoid} locale={locale} />}

      {variant === "a"
        ? (
          <>
            <p className="pm-home-intro">{intro}</p>
            <GalleryA {...photoProps} />
            {nameFromPhotoButton}
            {error && <ErrorNotice error={error} />}
            <BarcodeListA {...barcodeProps} />
          </>
        )
        : (
          <>
            <CompactB {...photoProps} intro={intro} />
            {nameFromPhotoButton}
            {error && <ErrorNotice error={error} />}
            <BarcodeChipsB {...barcodeProps} />
          </>
        )}

      <IonList inset>
        <IonItem lines="none">
          <Ban slot="start" size={22} className={`pm-row-icon ${media.avoid ? "pm-danger-icon" : "pm-muted-icon"}`} aria-hidden="true" />
          <IonToggle
            checked={media.avoid !== null}
            disabled={busy}
            color="danger"
            onIonChange={(event) => { const next = event.detail.checked; void run(() => actions.setAvoid(next)); }}
          >
            <span className="pm-wrap">{t("productScan.avoidToggle")}</span>
          </IonToggle>
        </IonItem>
      </IonList>
      <Footnote>{t("productScan.avoidToggleNote")} {t("productScan.deviceNote")}</Footnote>

      <BarcodeScanModal isOpen={scanning} onClose={() => setScanning(false)} onCode={addBarcode} />
      {naming.dialog}
      <IonActionSheet
        isOpen={confirmPhoto !== null}
        header={t("productScan.deletePhotoConfirm")}
        onDidDismiss={() => setConfirmPhoto(null)}
        buttons={[
          { text: t("productScan.deletePhoto"), role: "destructive", handler: () => { const id = confirmPhoto; if (id) void run(() => actions.removePhoto(id)); } },
          { text: t("ui.cancel"), role: "cancel" }
        ]}
      />
      <IonActionSheet
        isOpen={confirmCode !== null}
        header={t("productScan.deleteBarcodeConfirm", { code: confirmCode ? formatBarcode(confirmCode) : "" })}
        onDidDismiss={() => setConfirmCode(null)}
        buttons={[
          { text: t("productScan.deleteBarcodeShort"), role: "destructive", handler: () => { const code = confirmCode; if (code) void run(() => actions.removeBarcode(code)); } },
          { text: t("ui.cancel"), role: "cancel" }
        ]}
      />
    </section>
  );
}

function ErrorNotice({ error }: { error: { text: string; otherProductId?: string } }) {
  const { t } = useTranslation();
  // The other product's card comes back to this one.
  const returnState = useReturnState();
  return (
    <Notice tone="danger" role="alert">
      <p>{error.text}</p>
      {error.otherProductId && (
        <ButtonLink to={`/products/${error.otherProductId}`} state={returnState} fill="clear" size="small">{t("productScan.barcodeTakenOpen")}</ButtonLink>
      )}
    </Notice>
  );
}

/** The red «Больше не покупать» mark, on the card and in the scan result. */
export function AvoidBanner({ avoid, locale }: { avoid: AvoidMark; locale: string }) {
  const { t } = useTranslation();
  return (
    <div className="pm-avoid-banner" role="note">
      <Ban size={24} aria-hidden="true" />
      <div>
        {t("productScan.avoidTitle")}
        <small>{t("productScan.avoidSince", { date: new Intl.DateTimeFormat(locale, { day: "numeric", month: "long" }).format(new Date(avoid.since)) })}</small>
      </div>
    </div>
  );
}

interface PhotoBlockProps {
  media: ProductMedia;
  productName: string;
  busy: boolean;
  onCamera: () => void;
  onLibrary: () => void;
  onMakeMain: (photoId: string) => void;
  onDelete: (photoId: string) => void;
}

/** Variant A: a big photo on top, thumbnails under it, actions under those. */
function GalleryA({ media, productName, busy, onCamera, onLibrary, onMakeMain, onDelete }: PhotoBlockProps) {
  const { t } = useTranslation();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const photos = media.photos;
  const selected = photos.find((photo) => photo.id === selectedId) ?? photos.find((photo) => photo.id === media.mainPhotoId) ?? photos[0] ?? null;

  return (
    <div className="pm-media-gallery">
      {selected ? (
        <div className="pm-media-gallery-main">
          <ProductPhotoImage photoId={selected.id} alt={t("productScan.photoAlt", { name: productName })} />
          {selected.id === media.mainPhotoId && <span className="pm-media-badge">{t("productScan.mainBadge")}</span>}
        </div>
      ) : (
        <EmptyState icon={<Camera />} title={t("productScan.noPhotosTitle")} description={t("productScan.noPhotosText")} headingLevel={3} />
      )}
      {photos.length > 1 && (
        <div className="pm-media-thumbs" role="group" aria-label={t("productScan.photosTitle")}>
          {photos.map((photo, index) => (
            <button
              key={photo.id}
              type="button"
              className="pm-media-thumb-button"
              aria-pressed={photo.id === selected?.id}
              aria-label={thumbLabel(t, index, photos.length, photo.id === media.mainPhotoId)}
              onClick={() => setSelectedId(photo.id)}
            >
              <ProductPhotoImage photoId={photo.id} alt="" />
            </button>
          ))}
        </div>
      )}
      <div className="pm-media-actions">
        <IonButton fill="clear" size="small" disabled={busy} onClick={onCamera}><Camera size={18} aria-hidden="true" />{t("productScan.takePhoto")}</IonButton>
        <IonButton fill="clear" size="small" disabled={busy} onClick={onLibrary}><ImagePlus size={18} aria-hidden="true" />{t("productScan.pickPhoto")}</IonButton>
        {selected && selected.id !== media.mainPhotoId && (
          <IonButton fill="clear" size="small" disabled={busy} onClick={() => onMakeMain(selected.id)}>{t("productScan.makeMain")}</IonButton>
        )}
        {selected && (
          <IonButton fill="clear" size="small" color="danger" disabled={busy} onClick={() => onDelete(selected.id)}>
            <Trash2 size={18} aria-hidden="true" />{t("productScan.deletePhoto")}
          </IonButton>
        )}
      </div>
    </div>
  );
}

/** Variant B: a small main photo beside the purchase count, then a strip of all photos. */
function CompactB({ media, productName, busy, onCamera, onLibrary, onMakeMain, onDelete, intro }: PhotoBlockProps & { intro: string }) {
  const { t } = useTranslation();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const photos = media.photos;
  const selected = photos.find((photo) => photo.id === selectedId) ?? null;

  return (
    <>
      <div className="pm-media-hero">
        <span className="pm-media-hero-photo">
          {media.mainPhotoId
            ? <ProductPhotoImage photoId={media.mainPhotoId} alt={t("productScan.photoAlt", { name: productName })} />
            : (
              <button type="button" className="pm-media-add" style={{ width: 96, height: 96 }} disabled={busy} onClick={onCamera} aria-label={t("productScan.takePhoto")}>
                <Camera size={28} aria-hidden="true" /><span>{t("productScan.noPhotoShort")}</span>
              </button>
            )}
        </span>
        <p>{intro}</p>
      </div>

      <SectionHeader action={(
        <IonButton fill="clear" size="small" disabled={busy} onClick={onCamera}>
          <Camera size={18} aria-hidden="true" />&nbsp;{t("productScan.takePhotoShort")}
        </IonButton>
      )}>
        {t("productScan.photosCount", { count: photos.length })}
      </SectionHeader>
      <div className="pm-media-strip" role="group" aria-label={t("productScan.photosTitle")}>
        {photos.map((photo, index) => (
          <button
            key={photo.id}
            type="button"
            className="pm-media-thumb-button"
            aria-pressed={photo.id === selectedId}
            aria-label={thumbLabel(t, index, photos.length, photo.id === media.mainPhotoId)}
            onClick={() => setSelectedId(photo.id === selectedId ? null : photo.id)}
          >
            <ProductPhotoImage photoId={photo.id} alt="" />
            {photo.id === media.mainPhotoId && <span className="pm-media-badge" aria-hidden="true">{t("productScan.mainBadge")}</span>}
          </button>
        ))}
        <button type="button" className="pm-media-add" disabled={busy} onClick={onLibrary} aria-label={t("productScan.addPhoto")}>
          <Plus size={22} aria-hidden="true" /><span aria-hidden="true">{t("productScan.addPhotoShort")}</span>
        </button>
      </div>
      {selected ? (
        <div className="pm-media-actions pm-media-actions-loose">
          {selected.id !== media.mainPhotoId && (
            <IonButton fill="clear" size="small" disabled={busy} onClick={() => { onMakeMain(selected.id); setSelectedId(null); }}>{t("productScan.makeMain")}</IonButton>
          )}
          <IonButton fill="clear" size="small" color="danger" disabled={busy} onClick={() => { onDelete(selected.id); setSelectedId(null); }}>
            <Trash2 size={18} aria-hidden="true" />{t("productScan.deletePhoto")}
          </IonButton>
        </div>
      ) : photos.length > 0 && <Footnote>{t("productScan.photoTapHint")}</Footnote>}
    </>
  );
}

function thumbLabel(t: TFunction, index: number, count: number, main: boolean) {
  const label = t("productScan.photoThumbAlt", { number: index + 1, count });
  return main ? `${label}, ${t("productScan.mainBadge").toLowerCase()}` : label;
}

interface BarcodeBlockProps {
  barcodes: ProductBarcode[];
  busy: boolean;
  onScan: () => void;
  onDelete: (code: string) => void;
}

/** Variant A: barcodes as list rows with a bin each. */
function BarcodeListA({ barcodes, busy, onScan, onDelete }: BarcodeBlockProps) {
  const { t } = useTranslation();
  return (
    <>
      <SectionHeader>{t("productScan.barcodesTitle")}</SectionHeader>
      <IonList inset className="pm-after-header">
        {barcodes.map((barcode) => (
          <IonItem key={barcode.code}>
            <ScanBarcode slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
            <IonLabel>
              <h3 className="pm-media-code-text">{formatBarcode(barcode.code)}</h3>
              <p>{t(`productScan.source.${barcode.source}`)}</p>
            </IonLabel>
            <IonButton
              slot="end"
              fill="clear"
              color="danger"
              className="pm-icon-button"
              disabled={busy}
              aria-label={t("productScan.deleteBarcode", { code: formatBarcode(barcode.code) })}
              onClick={() => onDelete(barcode.code)}
            >
              <Trash2 size={20} aria-hidden="true" />
            </IonButton>
          </IonItem>
        ))}
        <IonItem button detail={false} lines="none" disabled={busy} onClick={onScan}>
          <ScanBarcode slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
          <IonLabel color="primary">{t("productScan.scanBarcode")}</IonLabel>
        </IonItem>
      </IonList>
      {barcodes.length === 0 && <Footnote>{t("productScan.noBarcodes")}</Footnote>}
    </>
  );
}

/** Variant B: barcodes as chips; tapping one asks whether to delete it. */
function BarcodeChipsB({ barcodes, busy, onScan, onDelete }: BarcodeBlockProps) {
  const { t } = useTranslation();
  return (
    <>
      <SectionHeader>{t("productScan.barcodesTitle")}</SectionHeader>
      <div className="pm-chip-row pm-padded">
        {barcodes.map((barcode) => (
          <button
            key={barcode.code}
            type="button"
            className="pm-media-chip pm-media-code"
            disabled={busy}
            aria-label={t("productScan.barcodeChip", { code: formatBarcode(barcode.code), source: t(`productScan.source.${barcode.source}`) })}
            onClick={() => onDelete(barcode.code)}
          >
            {formatBarcode(barcode.code)}
          </button>
        ))}
        <button type="button" className="pm-media-chip pm-media-chip-action" disabled={busy} onClick={onScan}>
          <ScanBarcode size={18} aria-hidden="true" />{t("productScan.scanShort")}
        </button>
      </div>
      <Footnote>{barcodes.length === 0 ? t("productScan.noBarcodes") : t("productScan.barcodeTapHint")}</Footnote>
    </>
  );
}
