import "fake-indexeddb/auto";
import { describe, expect, it, vi } from "vitest";
import { ApiError } from "../../api/client";
import { createMemoryStorage, type BatchKeyValue } from "../receipts/pendingBatchStore";
import { moveDeviceMediaToServer, openDeviceMediaStorage, type DeviceMediaTarget } from "./deviceMediaMove";

/** What stage 1b left on a phone: two products, photo bytes, the code index and remembered codes. */
async function stage1bPhone(): Promise<BatchKeyValue> {
  const storage = createMemoryStorage();
  await storage.put("media:kefir", {
    photos: [
      { id: "p1", createdAt: 1, width: 10, height: 10, size: 4 },
      { id: "p2", createdAt: 2, width: 10, height: 10, size: 4 }
    ],
    mainPhotoId: "p2",
    barcodes: [{ code: "5900512320045", source: "scan", addedAt: 1 }, { code: "04252614", source: "manual", addedAt: 2 }],
    avoid: { since: Date.UTC(2026, 9, 20) }
  });
  await storage.put("photo:p1", { bytes: new Uint8Array([1, 2, 3, 4]).buffer, type: "image/jpeg" });
  await storage.put("photo:p2", { bytes: new Uint8Array([5, 6, 7, 8]).buffer, type: "image/jpeg" });
  await storage.put("barcode:5900512320045", "kefir");
  await storage.put("barcode:04252614", "kefir");
  await storage.put("media:merged-away", { photos: [], mainPhotoId: null, barcodes: [{ code: "96385074", source: "scan", addedAt: 1 }], avoid: null });
  await storage.put("barcode:96385074", "merged-away");
  await storage.put("remembered", [{ code: "5901234123457", rememberedAt: 3, off: { name: "Mleko", brand: null, quantity: null, imageUrl: null } }]);
  return storage;
}

function server(overrides: Partial<DeviceMediaTarget> = {}): DeviceMediaTarget & { [K in keyof DeviceMediaTarget]: ReturnType<typeof vi.fn> } {
  let photoCount = 0;
  return {
    uploadPhoto: vi.fn(async (_productId: string, _photo: Blob, clientId: string) => {
      photoCount += 1;
      return { photo: { id: `server-${clientId}` }, created: true, firstPhoto: photoCount === 1 };
    }),
    setMainPhoto: vi.fn(async () => undefined),
    addBarcode: vi.fn(async (productId: string) => {
      if (productId === "merged-away") throw new ApiError("gone", 404, null, null, "productNotFound");
      return { created: true };
    }),
    setAvoid: vi.fn(async () => undefined),
    remember: vi.fn(async () => undefined),
    ...overrides
  } as never;
}

describe("moving stage 1b device media to the server", () => {
  it("uploads everything once, keeps the phone's main photo and empties the device store", async () => {
    const storage = await stage1bPhone();
    const target = server();

    const summary = await moveDeviceMediaToServer(storage, target);

    expect(target.uploadPhoto).toHaveBeenCalledTimes(2);
    // The device photo id goes along, so a repeat returns the stored copy.
    expect(target.uploadPhoto.mock.calls.map((call) => [call[0], call[2]])).toEqual([["kefir", "p1"], ["kefir", "p2"]]);
    expect(target.setMainPhoto).toHaveBeenCalledWith("kefir", "server-p2");
    // An old 8-digit UPC-E goes up as the EAN-13 it equals.
    expect(target.addBarcode).toHaveBeenCalledWith("kefir", "0042100005264", "manual");
    expect(target.setAvoid).toHaveBeenCalledWith("kefir", true, "2026-10-20T00:00:00.000Z");
    expect(target.remember).toHaveBeenCalledWith("5901234123457", { name: "Mleko", brand: null, quantity: null, imageUrl: null });
    // The merged-away product's code can never go up: skipped, not kept.
    expect(summary).toEqual({ moved: 6, skipped: 1, pending: 0 });
    expect(await storage.keys!()).toEqual([]);
  });

  it("keeps the phone's main choice only when the server had no photos before", async () => {
    const storage = await stage1bPhone();
    const target = server({ uploadPhoto: vi.fn(async (_productId: string, _photo: Blob, clientId: string) => ({ photo: { id: `server-${clientId}` }, created: true, firstPhoto: false })) });

    await moveDeviceMediaToServer(storage, target);

    expect(target.setMainPhoto).not.toHaveBeenCalled();
  });

  it("stops at a network failure, keeps the rest and finishes on the next run without duplicates", async () => {
    const storage = await stage1bPhone();
    const offline = server({
      uploadPhoto: vi.fn(async (_productId: string, _photo: Blob, clientId: string) => {
        if (clientId === "p2") throw new TypeError("Failed to fetch");
        return { photo: { id: `server-${clientId}` }, created: true, firstPhoto: true };
      })
    });

    const first = await moveDeviceMediaToServer(storage, offline);

    expect(first).toEqual({ moved: 1, skipped: 0, pending: 4 });
    expect(offline.addBarcode).not.toHaveBeenCalled();
    expect(offline.remember).not.toHaveBeenCalled();
    // p1 is on the server: its bytes are gone from the phone; p2 waits with the codes and the mark.
    expect(await storage.get("photo:p1")).toBeUndefined();
    expect(await storage.get("photo:p2")).toBeDefined();
    expect(await storage.get("media:kefir")).toMatchObject({ photos: [{ id: "p2" }], barcodes: [{ code: "5900512320045" }, { code: "04252614" }], avoid: { since: Date.UTC(2026, 9, 20) } });

    const online = server();
    const second = await moveDeviceMediaToServer(storage, online);

    expect(online.uploadPhoto.mock.calls.map((call) => call[2])).toEqual(["p2"]);
    expect(online.addBarcode).toHaveBeenCalledTimes(3);
    expect(second.pending).toBe(0);
    expect(await storage.keys!()).toEqual([]);
  });

  it("skips codes on another product and broken photos quietly, and still clears them", async () => {
    const storage = await stage1bPhone();
    const target = server({
      uploadPhoto: vi.fn(async (_productId: string, _photo: Blob, clientId: string) => {
        if (clientId === "p1") throw new ApiError("not a picture", 415, null, null, "photoNotAnImage");
        return { photo: { id: `server-${clientId}` }, created: true, firstPhoto: true };
      }),
      addBarcode: vi.fn(async (_productId: string, code: string) => {
        if (code === "5900512320045") throw new ApiError("taken", 409, null, null, "barcodeTaken", "other");
        return { created: true };
      }),
      remember: vi.fn(async () => { throw new ApiError("taken", 409, null, null, "barcodeTaken", "other"); })
    });

    const summary = await moveDeviceMediaToServer(storage, target);

    expect(summary).toEqual({ moved: 4, skipped: 3, pending: 0 });
    expect(await storage.keys!()).toEqual([]);
  });

  it("does nothing without stored data or without IndexedDB", async () => {
    const target = server();
    await expect(moveDeviceMediaToServer(null, target)).resolves.toEqual({ moved: 0, skipped: 0, pending: 0 });
    await expect(moveDeviceMediaToServer(createMemoryStorage(), target)).resolves.toEqual({ moved: 0, skipped: 0, pending: 0 });
    expect(target.uploadPhoto).not.toHaveBeenCalled();
  });

  it("reads the stage 1b IndexedDB store with its keys", async () => {
    const storage = await openDeviceMediaStorage();
    await storage!.put("media:kefir", { photos: [], mainPhotoId: null, barcodes: [], avoid: { since: 5 } });
    const target = server();

    expect(await moveDeviceMediaToServer(storage, target)).toEqual({ moved: 1, skipped: 0, pending: 0 });
    expect(await storage!.keys!()).toEqual([]);
  });
});
