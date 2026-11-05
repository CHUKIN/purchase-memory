import { Package } from "lucide-react";
import { useRef, type ChangeEvent } from "react";
import { usePhotoUrl } from "./useProductMedia";
import "./productScan.css";

/**
 * One stored product photo; a grey package tile while it loads or when it is
 * gone. The screens show photos at most ~100 px wide, so the small thumbnail
 * (~256 px) is the default; the full copy is fetched only where it is needed.
 */
export function ProductPhotoImage({ photoId, alt, className = "", variant = "thumbnail" }: {
  photoId: string | null | undefined;
  alt: string;
  className?: string;
  variant?: "photo" | "thumbnail";
}) {
  const { url } = usePhotoUrl(photoId, variant);
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
 * Renders nothing without a photo, so rows without photos look as before;
 * `placeholder` keeps a grey tile instead, so a list where some rows have
 * photos stays aligned. Lists pass the `mainPhotoId` they already got from
 * the server, so there is no request per row.
 */
export function ProductThumb({ photoId, slot = "start", placeholder = false }: { photoId: string | null | undefined; slot?: string; placeholder?: boolean }) {
  if (!photoId && !placeholder) return null;
  return (
    <span slot={slot} className="pm-media-thumb" aria-hidden="true">
      <ProductPhotoImage photoId={photoId} alt="" />
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
