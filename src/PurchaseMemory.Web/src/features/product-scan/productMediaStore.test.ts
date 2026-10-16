import "fake-indexeddb/auto";
import { describe, expect, it } from "vitest";
import { createMemoryStorage, openIndexedDbKeyValue } from "../receipts/pendingBatchStore";
import { createProductMediaStore, MediaStorageUnavailableError } from "./productMediaStore";

function memoryStore() {
  const storage = createMemoryStorage();
  let id = 0;
  let clock = 1_000;
  return createProductMediaStore(async () => storage, () => clock++, () => `p${++id}`);
}

const jpeg = (content: string) => ({ blob: new Blob([content], { type: "image/jpeg" }), width: 10, height: 20 });

describe("product media store", () => {
  it("makes the first photo main and moves «main» on when it is deleted", async () => {
    const store = memoryStore();
    await store.addPhoto("milk", jpeg("one"));
    await store.addPhoto("milk", jpeg("two"));
    expect((await store.getProductMedia("milk")).mainPhotoId).toBe("p1");

    await store.setMainPhoto("milk", "p2");
    expect((await store.getProductMedia("milk")).mainPhotoId).toBe("p2");
    await store.removePhoto("milk", "p2");

    const media = await store.getProductMedia("milk");
    expect(media.photos.map((photo) => photo.id)).toEqual(["p1"]);
    expect(media.mainPhotoId).toBe("p1");
    expect(await store.getPhotoBlob("p2")).toBeNull();
    expect(await (await store.getPhotoBlob("p1"))?.text()).toBe("one");
  });

  it("ties a barcode to one product only and forgets it from «remembered»", async () => {
    const store = memoryStore();
    await store.rememberBarcode("5900512320045", null);
    expect(await store.addBarcode("milk", "5900512320045", "scan")).toEqual({ ok: true });
    expect(await store.addBarcode("milk", "5900512320045", "manual")).toEqual({ ok: true });
    expect((await store.getProductMedia("milk")).barcodes).toHaveLength(1);
    expect(await store.findProductByBarcode("5900512320045")).toBe("milk");
    expect(await store.listRememberedBarcodes()).toEqual([]);

    expect(await store.addBarcode("kefir", "5900512320045", "scan")).toEqual({ ok: false, reason: "taken", productId: "milk" });

    await store.removeBarcode("milk", "5900512320045");
    expect(await store.findProductByBarcode("5900512320045")).toBeNull();
    expect(await store.addBarcode("kefir", "5900512320045", "scan")).toEqual({ ok: true });
  });

  it("keeps remembered codes newest first without duplicates", async () => {
    const store = memoryStore();
    await store.rememberBarcode("111", null);
    await store.rememberBarcode("222", { name: "Kefir", brand: null, quantity: null, imageUrl: null });
    await store.rememberBarcode("111", null);
    expect((await store.listRememberedBarcodes()).map((item) => item.code)).toEqual(["111", "222"]);
    expect((await store.findRememberedBarcode("222"))?.off?.name).toBe("Kefir");
    await store.forgetBarcode("222");
    expect(await store.findRememberedBarcode("222")).toBeNull();
  });

  it("marks and unmarks «Больше не покупать», keeping the first date", async () => {
    const store = memoryStore();
    await store.setAvoid("milk", true);
    const since = (await store.getProductMedia("milk")).avoid?.since;
    await store.setAvoid("milk", true);
    expect((await store.getProductMedia("milk")).avoid?.since).toBe(since);
    await store.setAvoid("milk", false);
    expect((await store.getProductMedia("milk")).avoid).toBeNull();
  });

  it("reads empty and refuses writes without storage", async () => {
    const store = createProductMediaStore(async () => null);
    expect(await store.getProductMedia("milk")).toEqual({ productId: "milk", photos: [], mainPhotoId: null, barcodes: [], avoid: null });
    await expect(store.setAvoid("milk", true)).rejects.toBeInstanceOf(MediaStorageUnavailableError);
    // A failed write does not block the next one.
    await expect(store.addBarcode("milk", "96385074", "manual")).rejects.toBeInstanceOf(MediaStorageUnavailableError);
  });

  it("keeps photo bytes and barcodes in IndexedDB across a fresh open, and clears them", async () => {
    const open = () => openIndexedDbKeyValue("product-media-test", "media");
    const store = createProductMediaStore(open);
    const photo = await store.addPhoto("milk", jpeg("bytes"));
    await store.addBarcode("milk", "96385074", "scan");

    const reopened = createProductMediaStore(open);
    expect(await (await reopened.getPhotoBlob(photo.id))?.text()).toBe("bytes");
    expect(await reopened.findProductByBarcode("96385074")).toBe("milk");

    await reopened.clear();
    expect(await reopened.getProductMedia("milk")).toEqual({ productId: "milk", photos: [], mainPhotoId: null, barcodes: [], avoid: null });
  });
});
