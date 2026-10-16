import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { scanVariantStorageKey, setScanVariant, useScanVariant } from "./scanVariant";

describe("scan variant", () => {
  afterEach(() => {
    act(() => setScanVariant("b"));
    window.localStorage.removeItem(scanVariantStorageKey);
  });

  it("is B by default, switches for every view and is kept on the device", () => {
    const first = renderHook(() => useScanVariant());
    const second = renderHook(() => useScanVariant());
    expect(first.result.current).toBe("b");

    act(() => setScanVariant("a"));

    expect(first.result.current).toBe("a");
    expect(second.result.current).toBe("a");
    expect(window.localStorage.getItem(scanVariantStorageKey)).toBe("a");
  });

  it("follows a change made in another tab", () => {
    const view = renderHook(() => useScanVariant());
    act(() => {
      window.localStorage.setItem(scanVariantStorageKey, "a");
      window.dispatchEvent(new StorageEvent("storage", { key: scanVariantStorageKey }));
    });
    expect(view.result.current).toBe("a");
  });
});
