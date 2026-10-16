import { afterEach, describe, expect, it, vi } from "vitest";
import { setAccessTokenSource, setSessionRejectedListener } from "../auth/authSession";
import { ApiError, classifyProduct, getProductCatalog, getPurchaseFeed, uploadReceipt } from "./client";

function jsonResponse(status: number, body: unknown, headers: Record<string, string> = {}) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json", ...headers } });
}

describe("API client authorization", () => {
  afterEach(() => {
    setAccessTokenSource(null);
    setSessionRejectedListener(null);
    vi.unstubAllGlobals();
  });

  it("sends the current session token as a bearer header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, { items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false, truncated: false }));
    vi.stubGlobal("fetch", fetchMock);
    setAccessTokenSource(async () => "jwt-anna");

    await getPurchaseFeed();

    const init = fetchMock.mock.calls[0][1] as RequestInit;
    expect(new Headers(init.headers).get("Authorization")).toBe("Bearer jwt-anna");
  });

  it("keeps the multipart body and adds the header to uploads", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(201, { receipt: {}, exactDuplicate: false }));
    vi.stubGlobal("fetch", fetchMock);
    setAccessTokenSource(async () => "jwt-anna");

    await uploadReceipt([new File(["x"], "receipt.jpg", { type: "image/jpeg" })]);

    const init = fetchMock.mock.calls[0][1] as RequestInit;
    expect(init.method).toBe("POST");
    expect(init.body).toBeInstanceOf(FormData);
    expect(new Headers(init.headers).get("Authorization")).toBe("Bearer jwt-anna");
    expect(fetchMock.mock.calls[0][0]).toBe("/api/receipts");
  });

  it("asks the server to queue recognition with the upload when requested", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(201, { receipt: {}, exactDuplicate: false }));
    vi.stubGlobal("fetch", fetchMock);

    await uploadReceipt([new File(["x"], "receipt.jpg", { type: "image/jpeg" })], { process: true });

    expect(fetchMock.mock.calls[0][0]).toBe("/api/receipts?process=true");
  });

  it("sends no header without a session", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, { items: [] }));
    vi.stubGlobal("fetch", fetchMock);

    await getPurchaseFeed();

    expect(fetchMock.mock.calls[0][1]).toBeUndefined();
  });

  it("reports a rejected session and exposes Retry-After on 429", async () => {
    const listener = vi.fn();
    setSessionRejectedListener(listener);
    vi.stubGlobal("fetch", vi.fn()
      .mockResolvedValueOnce(jsonResponse(401, { title: "Unauthorized" }))
      .mockResolvedValueOnce(jsonResponse(429, { title: "Too many receipt uploads" }, { "Retry-After": "120" })));

    await expect(getPurchaseFeed()).rejects.toBeInstanceOf(ApiError);
    expect(listener).toHaveBeenCalledWith(401, null);

    const error = await uploadReceipt([new File(["x"], "r.jpg", { type: "image/jpeg" })]).catch((caught: unknown) => caught);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(429);
    expect((error as ApiError).retryAfterSeconds).toBe(120);
  });
});

describe("API client categories", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("sends only the classification parts that are being changed", async () => {
    const fetchMock = vi.fn().mockImplementation(async () => jsonResponse(200, {}));
    vi.stubGlobal("fetch", fetchMock);

    await classifyProduct("p1", { categoryId: "milk" });
    await classifyProduct("p1", { brand: null });

    expect(JSON.parse(fetchMock.mock.calls[0][1].body as string)).toEqual({ setCategory: true, categoryId: "milk", setBrand: false, brand: null });
    expect(JSON.parse(fetchMock.mock.calls[1][1].body as string)).toEqual({ setCategory: false, categoryId: null, setBrand: true, brand: null });
    expect(fetchMock.mock.calls[0][0]).toBe("/api/products/p1/classification");
  });

  it("maps the category filter to the catalog query", async () => {
    const fetchMock = vi.fn().mockImplementation(async () => jsonResponse(200, { items: [] }));
    vi.stubGlobal("fetch", fetchMock);

    await getProductCatalog({ category: "none", brand: " Łaciate " });
    await getProductCatalog({ category: "dairy" });

    expect(fetchMock.mock.calls[0][0]).toBe("/api/products?uncategorized=true&brand=%C5%81aciate");
    expect(fetchMock.mock.calls[1][0]).toBe("/api/products?categoryId=dairy");
  });

  it("sends the «проверить» filter and the excluded product to the catalog, not the feed", async () => {
    const fetchMock = vi.fn().mockImplementation(async () => jsonResponse(200, { items: [] }));
    vi.stubGlobal("fetch", fetchMock);

    await getProductCatalog({ needsReview: true });
    await getProductCatalog({ query: "kefir", excludeProductId: "p1", pageSize: 8 });
    await getPurchaseFeed({ query: "kefir" });

    expect(fetchMock.mock.calls[0][0]).toBe("/api/products?needsReview=true");
    expect(fetchMock.mock.calls[1][0]).toBe("/api/products?q=kefir&excludeProductId=p1&pageSize=8");
    expect(fetchMock.mock.calls[2][0]).toBe("/api/purchases?q=kefir");
  });
});
