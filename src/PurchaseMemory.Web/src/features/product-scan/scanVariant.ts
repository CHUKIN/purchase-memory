import { useSyncExternalStore } from "react";

/**
 * Which of the two looks for product photos and scanning this device shows,
 * while the owner tries both (Настройки → «Вид фото и сканера»).
 *
 * - `a`: big photo gallery on the product card, two equal tiles «Чек» /
 *   «Сканировать» on Главная, the scan result as a full screen.
 * - `b`: compact card with a photo strip, one «Сканировать штрихкод» button
 *   under the receipt button, the scan result as a sheet over the camera.
 */
export type ScanVariant = "a" | "b";

export const scanVariantStorageKey = "purchase-memory.scan-variant";
const defaultVariant: ScanVariant = "b";
const listeners = new Set<() => void>();

function read(): ScanVariant {
  try {
    return window.localStorage.getItem(scanVariantStorageKey) === "a" ? "a" : defaultVariant;
  } catch {
    return defaultVariant;
  }
}

let current: ScanVariant | null = null;

function snapshot(): ScanVariant {
  current ??= read();
  return current;
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  const onStorage = (event: StorageEvent) => {
    if (event.key !== scanVariantStorageKey) return;
    current = read();
    listener();
  };
  window.addEventListener("storage", onStorage);
  return () => {
    listeners.delete(listener);
    window.removeEventListener("storage", onStorage);
  };
}

export function setScanVariant(variant: ScanVariant) {
  try {
    window.localStorage.setItem(scanVariantStorageKey, variant);
  } catch {
    // Storage blocked: the choice lasts for this visit.
  }
  current = variant;
  listeners.forEach((listener) => listener());
}

export function useScanVariant(): ScanVariant {
  return useSyncExternalStore(subscribe, snapshot, () => defaultVariant);
}
