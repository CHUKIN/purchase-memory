import type { BoundingBox } from "../../api/types";

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
