import type { BoundingBox, TextPolygon } from "../../api/types";

/**
 * The stored receipt photo a recognized value can be pointed at.
 *
 * Bounding boxes are fractions (0..1) of one image, x/y being the top-left
 * corner. Recognition does not say which photo of a multi-part receipt a box
 * belongs to, so such a receipt is not `croppable`: the first photo is shown
 * whole and nothing is highlighted rather than highlighting the wrong spot.
 */
export interface ReceiptPhoto {
  url: string;
  croppable: boolean;
}

/**
 * The backend already validates every box, but the UI must not build a broken
 * crop out of a response it did not expect.
 */
export function isUsableBox(box: BoundingBox | null | undefined): box is BoundingBox {
  if (!box) return false;
  const values = [box.x, box.y, box.width, box.height];
  return (
    values.every((value) => typeof value === "number" && Number.isFinite(value)) &&
    box.x >= 0 &&
    box.y >= 0 &&
    box.width > 0 &&
    box.height > 0 &&
    box.x + box.width <= 1.001 &&
    box.y + box.height <= 1.001
  );
}

/** The outlines the UI can draw: at least three points each, every value a fraction of the photo. */
export function usableShapes(shapes: TextPolygon[] | null | undefined): TextPolygon[] {
  if (!Array.isArray(shapes)) return [];
  return shapes.filter(
    (shape) =>
      Array.isArray(shape) &&
      shape.length >= 6 &&
      shape.length % 2 === 0 &&
      shape.every((value) => typeof value === "number" && Number.isFinite(value) && value >= 0 && value <= 1)
  );
}

/** The axis-aligned box around all outlines (they are already padded around the words). */
export function shapesBounds(shapes: TextPolygon[]): BoundingBox | null {
  if (shapes.length === 0) return null;
  let left = 1;
  let top = 1;
  let right = 0;
  let bottom = 0;
  for (const shape of shapes) {
    for (let index = 0; index < shape.length; index += 2) {
      left = Math.min(left, shape[index]);
      right = Math.max(right, shape[index]);
      top = Math.min(top, shape[index + 1]);
      bottom = Math.max(bottom, shape[index + 1]);
    }
  }
  return right > left && bottom > top ? { x: left, y: top, width: right - left, height: bottom - top } : null;
}

/**
 * What a line's highlight points at: its printed words when the server found
 * them, otherwise the recognition box, otherwise nothing (the whole photo).
 * Multi-photo receipts never get one (see `ReceiptPhoto`).
 */
export type LineHighlight =
  | { kind: "text"; shapes: TextPolygon[]; bounds: BoundingBox }
  | { kind: "box"; box: BoundingBox };

export function lineHighlight(
  photo: ReceiptPhoto,
  box: BoundingBox | null | undefined,
  shapes: TextPolygon[] | null | undefined
): LineHighlight | null {
  if (!photo.croppable) return null;
  const usable = usableShapes(shapes);
  const bounds = shapesBounds(usable);
  if (bounds) return { kind: "text", shapes: usable, bounds };
  return isUsableBox(box) ? { kind: "box", box } : null;
}

/** The region a highlight covers, for the crop window. */
export function highlightBounds(highlight: LineHighlight): BoundingBox {
  return highlight.kind === "text" ? highlight.bounds : highlight.box;
}

/** Whether a line can be placed on the photo at all (words or a usable box). */
export function hasLocation(box: BoundingBox | null | undefined, shapes: TextPolygon[] | null | undefined): boolean {
  return usableShapes(shapes).length > 0 || isUsableBox(box);
}

/** Widens a tight row box a little so the fragment keeps some visual context. */
export function padBox(box: BoundingBox | null | undefined): BoundingBox | null {
  if (!isUsableBox(box)) return null;

  const paddingX = 0.02;
  const paddingY = 0.015;
  const x = Math.max(0, box.x - paddingX);
  const y = Math.max(0, box.y - paddingY);
  return {
    x,
    y,
    width: Math.min(1 - x, box.width + (box.x - x) + paddingX),
    height: Math.min(1 - y, box.height + (box.y - y) + paddingY)
  };
}

/** The whole photo as a window. */
export const wholePhoto: BoundingBox = { x: 0, y: 0, width: 1, height: 1 };

/**
 * The part of the photo shown around a line in a space of the given shape
 * (`aspect` = width / height in pixels): the line spans the width, with as
 * many neighbouring rows above and below as fit, but no more than a band of
 * the receipt, so the line stays what you look at. Always inside the photo.
 */
export function fitWindow(box: BoundingBox, photoRatio: number, aspect: number): BoundingBox {
  const x = Math.max(0, box.x - 0.04);
  const right = Math.min(1, box.x + box.width + 0.04);
  const width = right - x;
  const wanted = (width * photoRatio) / Math.max(aspect, 0.1);
  const band = Math.max(0.3, box.height * 10);
  const height = Math.min(1, Math.max(box.height * 1.5, Math.min(wanted, band)));
  const centre = box.y + box.height / 2;
  const y = Math.min(Math.max(0, centre - height / 2), 1 - height);
  return { x, y, width, height };
}

/**
 * The pixel size of a window of the photo shown as large as possible inside a
 * `width` × `height` space without distortion.
 */
export function fitSize(window: BoundingBox, photoRatio: number, width: number, height: number) {
  const ratio = (photoRatio * window.width) / window.height;
  return width / height > ratio
    ? { width: height * ratio, height }
    : { width, height: width / ratio };
}

/** Where `box` sits inside `frame`, in percent of the frame, for CSS positioning. */
export function relativeTo(box: BoundingBox, frame: BoundingBox) {
  return {
    left: `${(100 * (box.x - frame.x)) / frame.width}%`,
    top: `${(100 * (box.y - frame.y)) / frame.height}%`,
    width: `${(100 * box.width) / frame.width}%`,
    height: `${(100 * box.height) / frame.height}%`
  };
}

/** Positions the whole photo so that only `frame` shows through a clipping parent. */
export function imageOffset(frame: BoundingBox) {
  return {
    width: `${100 / frame.width}%`,
    left: `${(-100 * frame.x) / frame.width}%`,
    top: `${(-100 * frame.y) / frame.height}%`
  };
}
