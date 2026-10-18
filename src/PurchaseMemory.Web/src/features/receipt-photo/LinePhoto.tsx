import { ZoomIn, ZoomOut } from "lucide-react";
import { useLayoutEffect, useRef, useState, type CSSProperties, type RefObject } from "react";
import { useTranslation } from "react-i18next";
import type { BoundingBox, TextPolygon } from "../../api/types";
import { fitSize, fitWindow, highlightBounds, imageOffset, lineHighlight, relativeTo, wholePhoto, type ReceiptPhoto } from "./photoBox";
import { TextHighlight } from "./TextHighlight";
import { usePinchZoom } from "./usePinchZoom";

/** The receipt photo as a screen has it: still loading, failed, or ready. */
export interface LineViewerPhoto {
  photo: ReceiptPhoto | null;
  failed: boolean;
}

export interface LinePhotoProps {
  photo: ReceiptPhoto;
  /** Where recognition placed the line; the fallback when its printed words are unknown. */
  box: BoundingBox | null | undefined;
  /** The line's printed words (Azure outlines); preferred over `box`. */
  shapes?: TextPolygon[] | null;
  /** Alt text for the fragment with the outlined line. */
  focusAlt: string;
  /** Alt text for the whole photo, shown when the line cannot be placed. */
  wholeAlt: string;
  /**
   * The photo's width/height once the browser has it. Kept by the caller so
   * moving between lines of the same photo does not measure again.
   */
  photoRatio: number | null;
  onPhotoRatio: (ratio: number) => void;
  /** Swipe navigation listens to this: it is off while the photo is zoomed. */
  onZoomChange?: (zoomed: boolean) => void;
  /** Changing it (another line) resets the zoom. */
  resetKey?: unknown;
  /** A short note over the top of the photo itself (e.g. «Место строки не найдено…»). */
  note?: string | null;
}

/** Whether `LinePhoto` will highlight this line on this photo. */
export function canHighlight(photo: ReceiptPhoto, box: BoundingBox | null | undefined, shapes?: TextPolygon[] | null): boolean {
  return lineHighlight(photo, box, shapes) !== null;
}

/** Until the photo answers, a typical receipt shape keeps the layout steady. */
const assumedRatio = 0.75;

/**
 * The receipt photo around one line, as large as the space it is given: the
 * line across the width with neighbouring rows above and below, highlighted
 * and the rest dimmed. The highlight follows the printed words when the server
 * found them (`shapes`, "combo" style), otherwise it is the recognition box. Pinch, drag and double-tap zoom it (or the corner button);
 * the outline zooms with the photo. Without a usable box (or on a multi-photo
 * receipt) it shows the whole photo with nothing outlined, so it never points
 * at the wrong place. The outline appears only once the photo's real
 * proportions are known.
 */
export function LinePhoto({ photo, box, shapes = null, focusAlt, wholeAlt, photoRatio, onPhotoRatio, onZoomChange, resetKey, note = null }: LinePhotoProps) {
  const { t } = useTranslation();
  const spaceRef = useRef<HTMLDivElement>(null);
  const space = useSpaceSize(spaceRef);
  const zoom = usePinchZoom({ resetKey, onZoomChange });
  const highlight = lineHighlight(photo, box, shapes);
  const highlighted = highlight !== null;
  const ratio = photoRatio ?? assumedRatio;
  const aspect = space ? space.width / space.height : 1.2;
  const view = highlight ? fitWindow(highlightBounds(highlight), ratio, aspect) : wholePhoto;
  const frameStyle: CSSProperties = space
    ? fitSize(view, ratio, space.width, space.height)
    : { width: "100%", aspectRatio: `${(ratio * view.width) / view.height}` };

  const measure = (image: HTMLImageElement) => {
    if (image.naturalWidth > 0 && image.naturalHeight > 0) onPhotoRatio(image.naturalWidth / image.naturalHeight);
  };

  return (
    <div className="pm-photo-space" ref={spaceRef}>
      <div
        className="pm-photo-frame"
        ref={zoom.frameRef}
        style={frameStyle}
        data-highlighted={highlighted}
        data-highlight={highlight?.kind ?? "none"}
        data-zoomed={zoom.zoomed}
        {...zoom.handlers}
      >
        <div className="pm-zoom-layer" style={zoom.layerStyle}>
          <img
            className="pm-crop-image"
            src={photo.url}
            alt={highlighted ? focusAlt : wholeAlt}
            draggable={false}
            onLoad={(event) => measure(event.currentTarget)}
            style={imageOffset(view)}
          />
          {highlight?.kind === "text" && photoRatio !== null && (
            <TextHighlight shapes={highlight.shapes} view={view} photoRatio={photoRatio} />
          )}
          {highlight?.kind === "box" && photoRatio !== null && (
            <div className="pm-locator-highlight" style={relativeTo(highlight.box, view)} aria-hidden="true" />
          )}
        </div>
        {/* Inside the frame: it stays on the photo however narrow it is, clear of the zoom button below. */}
        {note && <p className="pm-review-photo-note">{note}</p>}
        <button
          type="button"
          className="pm-photo-zoom"
          onClick={zoom.toggle}
          // A press on the button is not a tap or a drag on the photo.
          onPointerDown={(event) => event.stopPropagation()}
          onPointerUp={(event) => event.stopPropagation()}
          aria-label={zoom.zoomed ? t("lineReview.zoomOut") : t("lineReview.zoomIn")}
        >
          {zoom.zoomed ? <ZoomOut size={18} aria-hidden="true" /> : <ZoomIn size={18} aria-hidden="true" />}
        </button>
      </div>
    </div>
  );
}

/** The size of the space the photo may fill; null until (or where) layout is not measurable. */
function useSpaceSize(ref: RefObject<HTMLDivElement | null>) {
  const [size, setSize] = useState<{ width: number; height: number } | null>(null);
  useLayoutEffect(() => {
    const element = ref.current;
    if (!element) return;
    const read = () => {
      // Layout size, not getBoundingClientRect: the review card may still be
      // mid-animation (translated and rotated), which would inflate the box.
      const width = element.clientWidth;
      const height = element.clientHeight;
      setSize((current) => {
        if (width < 1 || height < 1) return null;
        return current && Math.abs(current.width - width) < 0.5 && Math.abs(current.height - height) < 0.5 ? current : { width, height };
      });
    };
    read();
    if (typeof ResizeObserver === "undefined") return;
    const observer = new ResizeObserver(read);
    observer.observe(element);
    return () => observer.disconnect();
  }, [ref]);
  return size;
}
