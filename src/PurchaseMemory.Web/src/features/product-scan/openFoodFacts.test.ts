import { afterEach, describe, expect, it, vi } from "vitest";
import { lookupOpenFoodFacts } from "./openFoodFacts";

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

describe("Open Food Facts lookup", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("asks our server, never Open Food Facts directly", async () => {
    const fetchMock = vi.fn(async () => json({ status: "found", product: { name: "Kefir", brand: "Piątnica", quantity: "1 l", imageUrl: null } }));
    vi.stubGlobal("fetch", fetchMock);

    await expect(lookupOpenFoodFacts("5900512320045")).resolves.toEqual({
      status: "found",
      product: { name: "Kefir", brand: "Piątnica", quantity: "1 l", imageUrl: null }
    });
    const [url] = fetchMock.mock.calls[0] as unknown as [string];
    expect(url).toBe("/api/open-food-facts/5900512320045");
  });

  it("passes «not found» and «not answering» on", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => json({ status: "notFound", product: null })));
    await expect(lookupOpenFoodFacts("5900512320045")).resolves.toEqual({ status: "notFound" });

    vi.stubGlobal("fetch", vi.fn(async () => json({ status: "error", product: null })));
    await expect(lookupOpenFoodFacts("5900512320045")).resolves.toEqual({ status: "error" });
  });

  it("reports «not answering» when our server fails or the limit is reached", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => json({ detail: "Too many lookups" }, 429)));
    await expect(lookupOpenFoodFacts("5900512320045")).resolves.toEqual({ status: "error" });

    vi.stubGlobal("fetch", vi.fn(async () => { throw new TypeError("offline"); }));
    await expect(lookupOpenFoodFacts("5900512320045")).resolves.toEqual({ status: "error" });
  });
});
