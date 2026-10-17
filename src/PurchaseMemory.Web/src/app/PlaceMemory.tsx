import { useEffect, useRef } from "react";
import { useLocation } from "react-router";
import { arrivalPlace, currentScroll, restoreScroll, savePlace } from "./place";

/**
 * Keeps «Назад» exact (see place.ts). On every tap anywhere it notes the
 * screen's scroll position for the current history entry (a stale open line
 * is dropped with it); when a screen is entered again it scrolls back to
 * where it was left.
 */
export function PlaceMemory() {
  const location = useLocation();
  const entryRef = useRef({ key: location.key, path: `${location.pathname}${location.search}` });
  entryRef.current = { key: location.key, path: `${location.pathname}${location.search}` };

  useEffect(() => {
    // Capture phase: runs before the tapped link navigates away.
    const note = () => savePlace(entryRef.current.key, entryRef.current.path, { scroll: currentScroll() });
    document.addEventListener("click", note, true);
    return () => document.removeEventListener("click", note, true);
  }, []);

  // Only on entering a screen: a filter or edit switch on the same screen keeps
  // its router state, and must not pull the scroll back.
  const shownPath = useRef<string | null>(null);
  useEffect(() => {
    if (shownPath.current === location.pathname) return;
    shownPath.current = location.pathname;
    const place = arrivalPlace(location.key, `${location.pathname}${location.search}`, location.state);
    if (!place?.scroll) return;
    return restoreScroll(place.scroll);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [location.pathname]);

  return null;
}
