import { describe, expect, it } from "vitest";
import { fitWindow, hasLocation, highlightBounds, lineHighlight, shapesBounds, usableShapes } from "./photoBox";

const photo = { url: "blob:receipt", croppable: true };
const box = { x: 0.1, y: 0.3, width: 0.8, height: 0.04 };
const words = [[0.2, 0.52, 0.6, 0.5, 0.6, 0.54, 0.2, 0.56], [0.55, 0.5, 0.8, 0.49, 0.8, 0.53, 0.55, 0.54]];

describe("text outlines on the photo", () => {
  it("keeps only outlines that can be drawn", () => {
    expect(usableShapes([
      words[0],
      [0.1, 0.1, 0.2, 0.2],
      [0.1, 0.1, 0.2, 0.2, 0.3],
      [0.1, 0.1, 0.2, 0.2, 1.4, 0.3],
      [0.1, Number.NaN, 0.2, 0.2, 0.3, 0.3]
    ])).toEqual([words[0]]);
    expect(usableShapes(null)).toEqual([]);
    expect(usableShapes(undefined)).toEqual([]);
  });

  it("finds the box around all outlines", () => {
    const bounds = shapesBounds(words)!;
    expect(bounds.x).toBeCloseTo(0.2);
    expect(bounds.y).toBeCloseTo(0.49);
    expect(bounds.width).toBeCloseTo(0.6);
    expect(bounds.height).toBeCloseTo(0.07);
    expect(shapesBounds([])).toBeNull();
  });

  it("prefers the printed words, falls back to the box, and never highlights a multi-photo receipt", () => {
    const text = lineHighlight(photo, box, words);
    expect(text?.kind).toBe("text");
    expect(highlightBounds(text!).y).toBeCloseTo(0.49);

    expect(lineHighlight(photo, box, null)).toEqual({ kind: "box", box });
    expect(lineHighlight(photo, box, [[0.1, 0.1]])).toEqual({ kind: "box", box });
    expect(lineHighlight(photo, null, null)).toBeNull();
    expect(lineHighlight({ ...photo, croppable: false }, box, words)).toBeNull();
  });

  it("knows whether a line can be placed at all", () => {
    expect(hasLocation(null, words)).toBe(true);
    expect(hasLocation(box, null)).toBe(true);
    expect(hasLocation(null, [])).toBe(false);
  });

  it("fits the crop window around the outlines", () => {
    const window = fitWindow(shapesBounds(words)!, 0.5, 1.2);
    expect(window.y).toBeLessThanOrEqual(0.49);
    expect(window.y + window.height).toBeGreaterThanOrEqual(0.56);
    expect(window.x).toBeCloseTo(0.16);
  });
});
