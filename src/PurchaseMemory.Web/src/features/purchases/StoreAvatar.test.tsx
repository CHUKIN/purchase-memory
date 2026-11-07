import { fireEvent, render } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { StoreAvatar, storeAvatarColors, storeAvatarPalette, storeInitial } from "./StoreAvatar";

function luminance(hex: string) {
  const channels = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255)
    .map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
  return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
}
function contrast(a: string, b: string) {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

describe("StoreAvatar", () => {
  it("shows the uppercase first letter, aria-hidden", () => {
    const { container } = render(<StoreAvatar name="biedronka" />);
    const avatar = container.querySelector(".pm-store-avatar");
    expect(avatar?.textContent).toBe("B");
    expect(avatar?.getAttribute("aria-hidden")).toBe("true");
  });

  it("shows the bundled logo for a known chain, aria-hidden", () => {
    const { container } = render(<StoreAvatar name="E.Leclerc URSYNÓW" />);
    const avatar = container.querySelector(".pm-store-avatar");
    expect(avatar?.getAttribute("aria-hidden")).toBe("true");
    expect(avatar?.querySelector("img")?.getAttribute("alt")).toBe("");
    expect(avatar?.textContent).toBe("");
  });

  it("falls back to the letter when the logo fails to load", () => {
    const { container } = render(<StoreAvatar name="Rossmann" />);
    fireEvent.error(container.querySelector("img")!);
    expect(container.querySelector("img")).toBeNull();
    expect(container.querySelector(".pm-store-avatar")?.textContent).toBe("R");
  });

  it("keeps the letter for a store without a logo", () => {
    const { container } = render(<StoreAvatar name="Dino Polska" />);
    expect(container.querySelector("img")).toBeNull();
    expect(container.querySelector(".pm-store-avatar")?.textContent).toBe("D");
  });

  it("handles Polish diacritics", () => {
    expect(storeInitial("żabka")).toBe("Ż");
    expect(storeInitial("Łukasz Market")).toBe("Ł");
  });

  it("shows a receipt icon for an unknown store", () => {
    for (const name of [null, undefined, "  "]) {
      const { container } = render(<StoreAvatar name={name} />);
      expect(container.querySelector(".pm-store-avatar-unknown svg")).not.toBeNull();
      expect(container.textContent).toBe("");
    }
  });

  it("picks the same colours for the same store, ignoring case", () => {
    expect(storeAvatarColors("Lidl")).toEqual(storeAvatarColors("LIDL"));
    expect(storeAvatarPalette).toContain(storeAvatarColors("Lidl"));
    const used = new Set(["Lidl", "Biedronka", "Żabka", "Rossmann", "Carrefour", "Auchan", "Kaufland", "Dino"].map((n) => storeAvatarColors(n).background));
    expect(used.size).toBeGreaterThan(2);
  });

  it("keeps every palette pair at 4.5:1 or better", () => {
    for (const { background, color } of storeAvatarPalette) {
      expect(contrast(background, color)).toBeGreaterThanOrEqual(4.5);
    }
  });
});
