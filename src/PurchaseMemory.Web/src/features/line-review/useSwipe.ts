import { useRef, type PointerEvent } from "react";

/** A horizontal drag longer than this (px) switches cards. */
export const swipeThreshold = 50;

/**
 * A mostly horizontal drag of at least `swipeThreshold` px, reported as the
 * step to take: to the left is the next card (+1), to the right the previous
 * one (−1), like turning pages. It ignores drags that start on a field or a
 * button (text selection and taps stay theirs), stops at a second finger (a
 * pinch) and is off while `disabled`. Vertical drags are left to scrolling.
 */
export function useSwipe(onStep: (step: 1 | -1) => void, disabled: boolean) {
  const start = useRef<{ id: number; x: number; y: number } | null>(null);
  const pointers = useRef(new Set<number>());

  const onPointerDown = (event: PointerEvent<HTMLElement>) => {
    pointers.current.add(event.pointerId);
    if (pointers.current.size > 1 || disabled || (event.target as HTMLElement).closest("input, textarea, select, button, a")) {
      start.current = null;
      return;
    }
    start.current = { id: event.pointerId, x: event.clientX, y: event.clientY };
  };
  const onPointerMove = (event: PointerEvent<HTMLElement>) => {
    const state = start.current;
    if (!state || state.id !== event.pointerId) return;
    const dx = event.clientX - state.x;
    const dy = event.clientY - state.y;
    // Clearly vertical: a scroll, not a swipe.
    if (Math.abs(dy) > 12 && Math.abs(dy) > Math.abs(dx)) start.current = null;
  };
  const onPointerEnd = (event: PointerEvent<HTMLElement>) => {
    pointers.current.delete(event.pointerId);
    const state = start.current;
    if (!state || state.id !== event.pointerId) return;
    start.current = null;
    if (event.type === "pointercancel") return;
    const dx = event.clientX - state.x;
    const dy = event.clientY - state.y;
    if (Math.abs(dx) < swipeThreshold || Math.abs(dx) <= Math.abs(dy)) return;
    onStep(dx < 0 ? 1 : -1);
  };

  return {
    handlers: {
      onPointerDown,
      onPointerMove,
      onPointerUp: onPointerEnd,
      onPointerCancel: onPointerEnd
    }
  };
}
