import { Package } from "lucide-react";
import { useRef, type ChangeEvent } from "react";
import { usePhotoUrl, useProductMedia } from "./useProductMedia";
import "./productScan.css";

/** One stored product photo; a grey package tile while it loads or when it is gone. */
export function ProductPhotoImage({ photoId, alt, className = "" }: { photoId: string | null | undefined; alt: string; className?: string }) {
  const { url } = usePhotoUrl(photoId);
  if (!url) {
    return (
      <span className={`pm-media-photo-empty ${className}`} role={alt ? "img" : undefined} aria-label={alt || undefined} aria-hidden={alt ? undefined : true}>
        <Package size={22} aria-hidden="true" />
      </span>
    );
  }
  return <img className={`pm-media-photo ${className}`} src={url} alt={alt} />;
}

/**
 * The product's main photo as a small square at the start of a list row.
 * Renders nothing when the product has no photo, so rows without photos look
 * as before; `placeholder` keeps a grey tile instead, so a list where some
 * rows have photos stays aligned.
 */
export function ProductThumb({ productId, slot = "start", placeholder = false }: { productId: string; slot?: string; placeholder?: boolean }) {
  const media = useProductMedia(productId);
  const mainPhotoId = media.data?.mainPhotoId ?? null;
  if (!mainPhotoId && !placeholder) return null;
  return (
    <span slot={slot} className="pm-media-thumb" aria-hidden="true">
      <ProductPhotoImage photoId={mainPhotoId} alt="" />
    </span>
  );
}

/**
 * Two hidden file inputs: the camera (`capture`) and the photo library. On an
 * iPhone, `capture` opens the camera at once; without it iOS offers the
 * library (and the camera too). Returns the inputs to render and openers.
 */
export function usePhotoPicker(onFile: (file: File) => void, labels: { camera: string; library: string }) {
  const cameraInput = useRef<HTMLInputElement>(null);
  const libraryInput = useRef<HTMLInputElement>(null);
  const onChange = (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (file) onFile(file);
  };
  const inputs = (
    <>
      <input ref={cameraInput} className="visually-hidden" tabIndex={-1} aria-label={labels.camera} type="file" accept="image/*" capture="environment" onChange={onChange} />
      <input ref={libraryInput} className="visually-hidden" tabIndex={-1} aria-label={labels.library} type="file" accept="image/*" onChange={onChange} />
    </>
  );
  return {
    inputs,
    openCamera: () => cameraInput.current?.click(),
    openLibrary: () => libraryInput.current?.click()
  };
}
