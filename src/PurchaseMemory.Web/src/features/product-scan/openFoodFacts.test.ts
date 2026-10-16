import { afterEach, describe, expect, it, vi } from "vitest";
import { lookupOpenFoodFacts } from "./openFoodFacts";

/** A fetch that answers only by failing once its signal aborts, like a server that never replies. */
function hangingFetch() {
  return vi.fn((_url: string, init?: RequestInit) => new Promise<Response>((_resolve, reject) => {
    init?.signal?.addEventListener("abort", () => reject(init.signal?.reason));
  }));
}

describe("Open Food Facts lookup", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("gives up after the timeout and reports «not answering»", async () => {
    vi.stubGlobal("fetch", hangingFetch());
    await expect(lookupOpenFoodFacts("5900512320045", undefined, 20)).resolves.toEqual({ status: "error" });
  });

  it("stops at once when the caller cancels", async () => {
    vi.stubGlobal("fetch", hangingFetch());
    const caller = new AbortController();
    const lookup = lookupOpenFoodFacts("5900512320045", caller.signal, 60_000);
    caller.abort();
    await expect(lookup).resolves.toEqual({ status: "error" });
  });

  it("reads a found product and keeps only safe image hosts", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify({
      status: 1,
      product: { product_name: " Kefir ", brands: "Piątnica", quantity: "1 l", image_front_small_url: "https://evil.example/x.jpg" }
    }))));
    await expect(lookupOpenFoodFacts("5900512320045")).resolves.toEqual({
      status: "found",
      product: { name: "Kefir", brand: "Piątnica", quantity: "1 l", imageUrl: null }
    });
  });

  it("treats 404 as an unknown code", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response("", { status: 404 })));
    await expect(lookupOpenFoodFacts("5900512320045")).resolves.toEqual({ status: "notFound" });
  });
});
