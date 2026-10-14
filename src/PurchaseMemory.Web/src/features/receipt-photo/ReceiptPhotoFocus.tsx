import type { BoundingBox } from "../../api/types";
import { contextWindow, imageOffset, isUsableBox, relativeTo, type ReceiptPhoto } from "./photoBox";

export interface ReceiptPhotoFocusProps {
  photo: ReceiptPhoto;
  /** Where the line is on the photo; anything unusable shows the whole photo. */
  box: BoundingBox | null | undefined;
  /** Alt text for the cropped fragment with the highlighted line. */
  focusAlt: string;
  /** Alt text for the whole photo, shown when the line cannot be placed. */
  wholeAlt: string;
  /**
   * The photo's width/height, once the browser has it. Kept by the caller so
   * moving between lines of the same photo does not have to measure again.
   */
  photoRatio: number | null;
  onPhotoRatio: (ratio: number) => void;
}

/** The tallest the photo strip may get, as a CSS length. */
const maxFrameHeight = "55vh";

/**
 * Size of the photo strip once the photo's proportions are known. The image
 * and the outline are positioned in percent of the strip, so the strip must
 * keep the exact aspect ratio of the window it shows: on a short (landscape)
 * screen it gets narrower instead of shorter.
 */
export function frameStyle(photoRatio: number, frame: BoundingBox) {
  const ratio = (photoRatio * frame.width) / frame.height;
  return { aspectRatio: `${ratio}`, width: `min(100%, calc(${maxFrameHeight} * ${ratio}))` };
}

/** Whether `ReceiptPhotoFocus` will highlight this box on this photo. */
export function canHighlight(photo: ReceiptPhoto, box: BoundingBox | null | undefined): box is BoundingBox {
  return photo.croppable && isUsableBox(box);
}

/**
 * The receipt photo around one line: a strip of the photo with the line and a
 * few rows around it, the line outlined. Without a usable box (or on a
 * multi-photo receipt) it degrades to the whole photo with nothing outlined,
 * so it never points at the wrong place.
 */
export function ReceiptPhotoFocus({ photo, box, focusAlt, wholeAlt, photoRatio, onPhotoRatio }: ReceiptPhotoFocusProps) {
  const measure = (image: HTMLImageElement) => {
    if (image.naturalWidth > 0 && image.naturalHeight > 0) onPhotoRatio(image.naturalWidth / image.naturalHeight);
  };

  if (!canHighlight(photo, box)) {
    return (
      <div className="pm-locator-photo" data-highlighted="false">
        <img className="pm-locator-whole" src={photo.url} alt={wholeAlt} onLoad={(event) => measure(event.currentTarget)} />
      </div>
    );
  }

  const frame = contextWindow(box);
  return (
    <div className="pm-locator-photo" data-highlighted="true">
      <div
        className="pm-locator-frame"
        // The strip keeps the photo's proportions; until they are known it
        // uses the CSS default and the outline waits, so it is never misplaced.
        style={photoRatio === null ? undefined : frameStyle(photoRatio, frame)}
      >
        <img
          className="pm-crop-image"
          src={photo.url}
          alt={focusAlt}
          onLoad={(event) => measure(event.currentTarget)}
          style={imageOffset(frame)}
        />
        {photoRatio !== null && <div className="pm-locator-highlight" style={relativeTo(box, frame)} aria-hidden="true" />}
      </div>
    </div>
  );
}
