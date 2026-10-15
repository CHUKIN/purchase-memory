import { useTranslation } from "react-i18next";
import { useLocation } from "react-router";
import { browserHistoryIndex, type BackTarget } from "../design-system/components";

/*
 * The «‹ Назад» button must name the screen the tap really returns to. A
 * screen that opens another one passes its own address in router state
 * (`returnTo`); the opened screen shows that screen's name and goes back to
 * it. Without that state (a deep link, a shared address) the button names
 * and opens the fixed parent screen instead of guessing from browser history.
 */
export interface ReturnState {
  /** The opening screen's address, path plus query (filters). */
  returnTo: string;
  /** Its position in the browser history, so «назад» can step back to it and keep its scroll. */
  returnIndex?: number;
}

/** Names of the screens a person can come back to. Detail screens fall back to «Назад». */
const screenLabelKeys: Record<string, string> = {
  "/": "common.home",
  "/purchases": "common.purchases",
  "/products": "common.products",
  "/analytics": "common.analytics",
  "/needs-attention": "common.review",
  "/scan": "productScan.scanTitle"
};

/** Router state for a link, so the opened screen can come back here. */
export function useReturnState(): ReturnState {
  const location = useLocation();
  const index = browserHistoryIndex();
  const returnTo = `${location.pathname}${location.search}`;
  return index === null ? { returnTo } : { returnTo, returnIndex: index };
}

/** Reads `returnTo` from router state; only in-app paths are accepted. */
export function readReturnState(state: unknown): ReturnState | null {
  if (typeof state !== "object" || state === null || !("returnTo" in state)) return null;
  const { returnTo, returnIndex } = state as { returnTo: unknown; returnIndex?: unknown };
  if (typeof returnTo !== "string" || !returnTo.startsWith("/") || returnTo.startsWith("//") || returnTo.startsWith("/\\")) return null;
  return typeof returnIndex === "number" && Number.isInteger(returnIndex) && returnIndex >= 0
    ? { returnTo, returnIndex }
    : { returnTo };
}

export function backLabelKey(path: string): string {
  const pathname = path.split(/[?#]/, 1)[0];
  return screenLabelKeys[pathname] ?? "ui.back";
}

/** The back button for a screen: the opening screen when known, otherwise `fallback`. */
export function useBackTarget(fallback: { to: string; labelKey: string }): BackTarget {
  const { t } = useTranslation();
  const origin = readReturnState(useLocation().state);
  if (!origin) return { to: fallback.to, label: t(fallback.labelKey) };
  return { to: origin.returnTo, label: t(backLabelKey(origin.returnTo)), historyIndex: origin.returnIndex };
}
