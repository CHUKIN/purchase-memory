import { useCallback, useSyncExternalStore } from "react";

/**
 * How a receipt line is shown on the photo (a per-device preference in
 * «Настройки»): `tap` shows the one line that was tapped, `swipe` opens a
 * viewer that goes through the lines one by one, starting at the tapped one.
 */
export type LineLocatorMode = "tap" | "swipe";

export const lineLocatorModes: readonly LineLocatorMode[] = ["tap", "swipe"];
export const defaultLineLocatorMode: LineLocatorMode = "tap";
export const lineLocatorStorageKey = "purchase-memory.line-locator-mode";

export function isLineLocatorMode(value: unknown): value is LineLocatorMode {
  return value === "tap" || value === "swipe";
}

const listeners = new Set<() => void>();
// Used when storage is unavailable, so the choice still lasts for this visit.
let memoryValue: LineLocatorMode | null = null;

// Storage can be missing or throw (private mode, blocked site data).
export function readLineLocatorMode(): LineLocatorMode {
  try {
    const stored = window.localStorage.getItem(lineLocatorStorageKey);
    return isLineLocatorMode(stored) ? stored : defaultLineLocatorMode;
  } catch {
    return memoryValue ?? defaultLineLocatorMode;
  }
}

export function writeLineLocatorMode(mode: LineLocatorMode) {
  memoryValue = mode;
  try {
    window.localStorage.setItem(lineLocatorStorageKey, mode);
  } catch {
    // The in-memory value above covers this visit.
  }
  listeners.forEach((listener) => listener());
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  // Another tab of the app changed the setting.
  const onStorage = (event: StorageEvent) => { if (event.key === lineLocatorStorageKey) listener(); };
  window.addEventListener("storage", onStorage);
  return () => {
    listeners.delete(listener);
    window.removeEventListener("storage", onStorage);
  };
}

/** The current mode and a setter; every screen using it updates together. */
export function useLineLocatorMode(): [LineLocatorMode, (mode: LineLocatorMode) => void] {
  const mode = useSyncExternalStore(subscribe, readLineLocatorMode, () => defaultLineLocatorMode);
  const setMode = useCallback((next: LineLocatorMode) => writeLineLocatorMode(next), []);
  return [mode, setMode];
}
