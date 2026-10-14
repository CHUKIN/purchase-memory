import { useCallback, useEffect, useRef, useState, type CSSProperties, type PointerEvent as ReactPointerEvent } from "react";

/** How far the photo is zoomed (1 = fits) and shifted, in pixels of the frame. */
export interface ZoomState {
  scale: number;
  x: number;
  y: number;
}

export const maxZoom = 5;
export const toggleZoom = 2.5;
const unzoomed: ZoomState = { scale: 1, x: 0, y: 0 };
/** Two taps closer than this (ms, px) are a double tap. */
const doubleTapMs = 300;
const doubleTapDistance = 30;
/** A finger that moved more than this is not a tap. */
const tapSlop = 8;

const clamp = (value: number, min: number, max: number) => Math.min(max, Math.max(min, value));

/**
 * Keeps the zoomed photo covering its frame: no empty gaps at the edges, and
 * back to exactly 1 when it is nearly there.
 */
export function clampZoom(state: ZoomState, width: number, height: number): ZoomState {
  const scale = clamp(state.scale, 1, maxZoom);
  if (scale <= 1.01) return unzoomed;
  return {
    scale,
    x: clamp(state.x, width - width * scale, 0),
    y: clamp(state.y, height - height * scale, 0)
  };
}

/** Zooms to `scale` keeping the photo point under (`px`, `py`) where it is. */
export function zoomAt(state: ZoomState, scale: number, px: number, py: number, width: number, height: number): ZoomState {
  const contentX = (px - state.x) / state.scale;
  const contentY = (py - state.y) / state.scale;
  return clampZoom({ scale, x: px - contentX * scale, y: py - contentY * scale }, width, height);
}

type Gesture =
  | { kind: "pinch"; distance: number; scale: number; contentX: number; contentY: number }
  | { kind: "pan"; startX: number; startY: number; fromX: number; fromY: number }
  | null;

/**
 * Pinch-zoom, pan and double-tap zoom for a photo frame, on plain pointer
 * events. The transform goes on one layer that holds both the photo and the
 * line outline, so the outline stays on its line at any zoom.
 */
export function usePinchZoom({ resetKey, onZoomChange }: { resetKey?: unknown; onZoomChange?: (zoomed: boolean) => void }) {
  const frameRef = useRef<HTMLDivElement>(null);
  const [zoom, setZoom] = useState<ZoomState>(unzoomed);
  const zoomRef = useRef(zoom);
  zoomRef.current = zoom;
  const pointers = useRef(new Map<number, { x: number; y: number }>());
  const gesture = useRef<Gesture>(null);
  const tap = useRef<{ startX: number; startY: number; at: number; moved: boolean; multi: boolean } | null>(null);
  const lastTap = useRef<{ x: number; y: number; at: number } | null>(null);

  // A new line starts unzoomed.
  const [lastReset, setLastReset] = useState(resetKey);
  if (lastReset !== resetKey) {
    setLastReset(resetKey);
    setZoom(unzoomed);
  }

  const zoomed = zoom.scale > 1;
  const notify = useRef(onZoomChange);
  notify.current = onZoomChange;
  useEffect(() => { notify.current?.(zoomed); }, [zoomed]);

  const rect = () => frameRef.current?.getBoundingClientRect() ?? null;
  const local = (x: number, y: number) => {
    const box = rect();
    return box ? { x: x - box.left, y: y - box.top, width: box.width || 1, height: box.height || 1 } : { x, y, width: 1, height: 1 };
  };

  const startPinch = () => {
    const [a, b] = [...pointers.current.values()];
    const middle = local((a.x + b.x) / 2, (a.y + b.y) / 2);
    const current = zoomRef.current;
    gesture.current = {
      kind: "pinch",
      distance: Math.max(1, Math.hypot(a.x - b.x, a.y - b.y)),
      scale: current.scale,
      contentX: (middle.x - current.x) / current.scale,
      contentY: (middle.y - current.y) / current.scale
    };
  };
  const startPan = (x: number, y: number) => {
    gesture.current = { kind: "pan", startX: x, startY: y, fromX: zoomRef.current.x, fromY: zoomRef.current.y };
  };

  const onPointerDown = (event: ReactPointerEvent<HTMLElement>) => {
    pointers.current.set(event.pointerId, { x: event.clientX, y: event.clientY });
    if (pointers.current.size === 1) {
      tap.current = { startX: event.clientX, startY: event.clientY, at: event.timeStamp, moved: false, multi: false };
      if (zoomRef.current.scale > 1) startPan(event.clientX, event.clientY);
    } else {
      if (tap.current) tap.current.multi = true;
      if (pointers.current.size === 2) startPinch();
    }
  };

  const onPointerMove = (event: ReactPointerEvent<HTMLElement>) => {
    if (!pointers.current.has(event.pointerId)) return;
    pointers.current.set(event.pointerId, { x: event.clientX, y: event.clientY });
    if (tap.current && Math.hypot(event.clientX - tap.current.startX, event.clientY - tap.current.startY) > tapSlop) tap.current.moved = true;
    const current = gesture.current;
    if (current?.kind === "pinch" && pointers.current.size >= 2) {
      const [a, b] = [...pointers.current.values()];
      const middle = local((a.x + b.x) / 2, (a.y + b.y) / 2);
      const scale = clamp((current.scale * Math.hypot(a.x - b.x, a.y - b.y)) / current.distance, 1, maxZoom);
      setZoom(clampZoom({ scale, x: middle.x - current.contentX * scale, y: middle.y - current.contentY * scale }, middle.width, middle.height));
    } else if (current?.kind === "pan") {
      const box = local(0, 0);
      setZoom(clampZoom({
        scale: zoomRef.current.scale,
        x: current.fromX + event.clientX - current.startX,
        y: current.fromY + event.clientY - current.startY
      }, box.width, box.height));
    }
  };

  const onPointerEnd = (event: ReactPointerEvent<HTMLElement>) => {
    if (!pointers.current.delete(event.pointerId)) return;
    if (pointers.current.size === 1) {
      // One finger left after a pinch: it pans from here.
      const [rest] = [...pointers.current.values()];
      if (zoomRef.current.scale > 1) startPan(rest.x, rest.y); else gesture.current = null;
      return;
    }
    if (pointers.current.size > 0) return;
    gesture.current = null;
    const finished = tap.current;
    tap.current = null;
    if (event.type === "pointercancel" || !finished || finished.moved || finished.multi) return;
    const previous = lastTap.current;
    if (previous && event.timeStamp - previous.at < doubleTapMs && Math.hypot(event.clientX - previous.x, event.clientY - previous.y) < doubleTapDistance) {
      lastTap.current = null;
      const point = local(event.clientX, event.clientY);
      setZoom(zoomRef.current.scale > 1 ? unzoomed : zoomAt(zoomRef.current, toggleZoom, point.x, point.y, point.width, point.height));
    } else {
      lastTap.current = { x: event.clientX, y: event.clientY, at: event.timeStamp };
    }
  };

  /** Zoom in on the middle, or back out (the button for mouse and keyboard). */
  const toggle = useCallback(() => {
    const box = frameRef.current?.getBoundingClientRect();
    const width = box?.width || 1;
    const height = box?.height || 1;
    setZoom((current) => (current.scale > 1 ? unzoomed : zoomAt(current, toggleZoom, width / 2, height / 2, width, height)));
  }, []);

  return {
    frameRef,
    zoom,
    zoomed,
    toggle,
    handlers: {
      onPointerDown,
      onPointerMove,
      onPointerUp: onPointerEnd,
      onPointerCancel: onPointerEnd
    },
    layerStyle: {
      transform: `translate(${zoom.x}px, ${zoom.y}px) scale(${zoom.scale})`,
      "--pm-zoom": String(zoom.scale)
    } as CSSProperties
  };
}
