/**
 * A product photo is kept as a small JPEG: the longest side at most 1280 px,
 * usually 150–250 KB. Enough to recognise a package on the shelf, small
 * enough for hundreds of products on a phone (and, from stage 2, in the
 * 1 GB of Supabase Storage).
 */
export const productPhotoLimits = {
  maxSide: 1280,
  quality: 0.8,
  /** Above this, try once more with a lower quality. */
  targetBytes: 260 * 1024,
  fallbackQuality: 0.65
} as const;

export class PhotoDecodeError extends Error {
  constructor() {
    super("The picked file could not be read as an image.");
    this.name = "PhotoDecodeError";
  }
}

export async function compressPhoto(file: Blob): Promise<{ blob: Blob; width: number; height: number }> {
  const image = await loadImage(file);
  const scale = Math.min(1, productPhotoLimits.maxSide / Math.max(image.naturalWidth, image.naturalHeight));
  const width = Math.max(1, Math.round(image.naturalWidth * scale));
  const height = Math.max(1, Math.round(image.naturalHeight * scale));
  const canvas = document.createElement("canvas");
  canvas.width = width;
  canvas.height = height;
  const context = canvas.getContext("2d");
  if (!context) throw new PhotoDecodeError();
  // JPEG has no transparency; a white backdrop beats black for a cut-out PNG.
  context.fillStyle = "#fff";
  context.fillRect(0, 0, width, height);
  context.drawImage(image, 0, 0, width, height);
  let blob = await toJpeg(canvas, productPhotoLimits.quality);
  if (blob.size > productPhotoLimits.targetBytes) blob = await toJpeg(canvas, productPhotoLimits.fallbackQuality);
  // Free the canvas memory at once; iOS caps the total canvas area per page.
  canvas.width = 0;
  canvas.height = 0;
  return { blob, width, height };
}

/**
 * Through an <img>, not createImageBitmap: browsers apply the EXIF rotation
 * of an iPhone photo to an <img> by default, so the result is upright.
 */
function loadImage(file: Blob): Promise<HTMLImageElement> {
  const url = URL.createObjectURL(file);
  const image = new Image();
  image.decoding = "async";
  return new Promise<HTMLImageElement>((resolve, reject) => {
    image.onload = () => resolve(image);
    image.onerror = () => reject(new PhotoDecodeError());
    image.src = url;
  }).finally(() => URL.revokeObjectURL(url));
}

function toJpeg(canvas: HTMLCanvasElement, quality: number): Promise<Blob> {
  return new Promise((resolve, reject) => {
    canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new PhotoDecodeError())), "image/jpeg", quality);
  });
}
