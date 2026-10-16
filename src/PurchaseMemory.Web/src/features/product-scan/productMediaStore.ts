import { legacyUpcECodes } from "./barcode";
import { openIndexedDbKeyValue, type BatchKeyValue } from "../receipts/pendingBatchStore";

/**
 * Product photos, barcodes, the «Больше не покупать» mark and barcodes
 * remembered for later — kept on this device only.
 *
 * There is no backend for these yet (stage 2 of ПЛАН_ФОТО_И_ШТРИХКОД_ТОВАРА.md
 * adds the tables and the API). Everything the screens need goes through
 * {@link ProductMediaStore}, so stage 2 replaces this one module with API calls
 * and the screens stay as they are. Until then the data is per device: the
 * other household member does not see it, and signing out clears it.
 *
 * Every call swallows storage errors where it can: private mode or a missing
 * IndexedDB mean nothing is kept, and reads return empty values. Writes that
 * cannot be kept reject, so the screen can say so.
 */

export type BarcodeSource = "scan" | "manual" | "openFoodFacts";

export interface ProductPhoto {
  id: string;
  createdAt: number;
  width: number;
  height: number;
  /** Bytes of the stored (already compressed) JPEG. */
  size: number;
}

export interface ProductBarcode {
  code: string;
  source: BarcodeSource;
  addedAt: number;
}

export interface AvoidMark {
  since: number;
}

export interface ProductMedia {
  productId: string;
  /** Oldest first, the order they were added in. */
  photos: ProductPhoto[];
  /** Always one of `photos` while there are any; null without photos. */
  mainPhotoId: string | null;
  barcodes: ProductBarcode[];
  /** «Больше не покупать»; null when not marked. */
  avoid: AvoidMark | null;
}

/** What Open Food Facts said about a barcode; a hint only, never trusted. */
export interface OffProductHint {
  name: string | null;
  brand: string | null;
  quantity: string | null;
  imageUrl: string | null;
}

/** A barcode scanned in the shop that is not tied to any product yet. */
export interface RememberedBarcode {
  code: string;
  rememberedAt: number;
  /** Null: Open Food Facts did not know it (or was not asked). */
  off: OffProductHint | null;
}

export type AddBarcodeResult =
  | { ok: true }
  /** The code already belongs to another product; one code, one product. */
  | { ok: false; reason: "taken"; productId: string };

export interface ProductMediaStore {
  getProductMedia: (productId: string) => Promise<ProductMedia>;
  /** The photo's JPEG, or null when it is gone. */
  getPhotoBlob: (photoId: string) => Promise<Blob | null>;
  /** Stores an already compressed photo; the first photo becomes the main one. */
  addPhoto: (productId: string, photo: { blob: Blob; width: number; height: number }) => Promise<ProductPhoto>;
  /** Removes a photo; if it was the main one, the oldest remaining becomes main. */
  removePhoto: (productId: string, photoId: string) => Promise<void>;
  setMainPhoto: (productId: string, photoId: string) => Promise<void>;
  /** Adding a code the product already has is a no-op success. Also forgets it from «remembered». */
  addBarcode: (productId: string, code: string, source: BarcodeSource) => Promise<AddBarcodeResult>;
  removeBarcode: (productId: string, code: string) => Promise<void>;
  /** The product this code belongs to, or null. */
  findProductByBarcode: (code: string) => Promise<string | null>;
  setAvoid: (productId: string, avoid: boolean) => Promise<void>;
  rememberBarcode: (code: string, off: OffProductHint | null) => Promise<void>;
  findRememberedBarcode: (code: string) => Promise<RememberedBarcode | null>;
  /** Newest first. */
  listRememberedBarcodes: () => Promise<RememberedBarcode[]>;
  forgetBarcode: (code: string) => Promise<void>;
  /**
   * Two products were merged on the server: the merged-away product's photos,
   * barcodes and «Больше не покупать» join the target's on this device. The
   * target keeps its main photo (the source's only fills an empty slot); a
   * mark on either product marks the target. Moving nothing is a no-op.
   */
  moveProductMedia: (fromProductId: string, toProductId: string) => Promise<void>;
  /** Everything, e.g. on sign-out. */
  clear: () => Promise<void>;
}

/** Thrown when the device refused to keep a change (no IndexedDB, full disk, private mode). */
export class MediaStorageUnavailableError extends Error {
  constructor() {
    super("Product media storage is unavailable on this device.");
    this.name = "MediaStorageUnavailableError";
  }
}

interface StoredMedia {
  photos: ProductPhoto[];
  mainPhotoId: string | null;
  barcodes: ProductBarcode[];
  avoid: AvoidMark | null;
}

interface StoredPhotoBytes {
  bytes: ArrayBuffer;
  type: string;
}

const mediaKey = (productId: string) => `media:${productId}`;
const photoKey = (photoId: string) => `photo:${photoId}`;
const barcodeKey = (code: string) => `barcode:${code}`;
const rememberedKey = "remembered";
/** A list, not a key per code, so it can be listed without enumerating keys. */
const maxRemembered = 200;

const emptyMedia = (productId: string): ProductMedia => ({ productId, photos: [], mainPhotoId: null, barcodes: [], avoid: null });

export function createProductMediaStore(
  openStorage: () => Promise<BatchKeyValue | null>,
  now: () => number = Date.now,
  newId: () => string = randomId,
  clearWaitMs = 2000
): ProductMediaStore {
  let storage: Promise<BatchKeyValue | null> | null = null;
  const kv = () => {
    storage ??= openStorage().catch(() => null);
    return storage;
  };
  // Writes are read-modify-write over several transactions; one at a time
  // keeps two quick taps from overwriting each other.
  let queue: Promise<unknown> = Promise.resolve();
  function write<T>(action: (store: BatchKeyValue) => Promise<T>): Promise<T> {
    const run = queue.then(async () => {
      const store = await kv();
      if (!store) throw new MediaStorageUnavailableError();
      return action(store);
    });
    queue = run.catch(() => undefined);
    return run;
  }

  async function readMedia(store: BatchKeyValue, productId: string): Promise<StoredMedia> {
    return asMedia(await store.get(mediaKey(productId)));
  }

  async function readRemembered(store: BatchKeyValue): Promise<RememberedBarcode[]> {
    const value = await store.get(rememberedKey);
    return Array.isArray(value) ? value.filter(isRemembered) : [];
  }

  async function forget(store: BatchKeyValue, code: string) {
    const list = await readRemembered(store);
    if (list.some((item) => item.code === code)) await store.put(rememberedKey, list.filter((item) => item.code !== code));
  }

  return {
    async getProductMedia(productId) {
      try {
        const store = await kv();
        if (!store) return emptyMedia(productId);
        return { productId, ...(await readMedia(store, productId)) };
      } catch {
        return emptyMedia(productId);
      }
    },

    async getPhotoBlob(photoId) {
      try {
        const store = await kv();
        const stored = asBytes(await store?.get(photoKey(photoId)));
        return stored ? new Blob([stored.bytes], { type: stored.type }) : null;
      } catch {
        return null;
      }
    },

    addPhoto(productId, photo) {
      return write(async (store) => {
        const entry: ProductPhoto = { id: newId(), createdAt: now(), width: photo.width, height: photo.height, size: photo.blob.size };
        // Bytes, not the Blob: older iOS versions lost Blobs kept in IndexedDB.
        await store.put(photoKey(entry.id), { bytes: await photo.blob.arrayBuffer(), type: photo.blob.type || "image/jpeg" } satisfies StoredPhotoBytes);
        try {
          const media = await readMedia(store, productId);
          media.photos.push(entry);
          media.mainPhotoId ??= entry.id;
          await store.put(mediaKey(productId), media);
        } catch (error) {
          // The list never got the photo; do not leave its bytes behind unreachable.
          await store.delete(photoKey(entry.id)).catch(() => undefined);
          throw error;
        }
        return entry;
      });
    },

    removePhoto(productId, photoId) {
      return write(async (store) => {
        const media = await readMedia(store, productId);
        media.photos = media.photos.filter((photo) => photo.id !== photoId);
        if (media.mainPhotoId === photoId || !media.photos.some((photo) => photo.id === media.mainPhotoId)) {
          media.mainPhotoId = media.photos[0]?.id ?? null;
        }
        await store.put(mediaKey(productId), media);
        await store.delete(photoKey(photoId));
      });
    },

    setMainPhoto(productId, photoId) {
      return write(async (store) => {
        const media = await readMedia(store, productId);
        if (!media.photos.some((photo) => photo.id === photoId)) return;
        media.mainPhotoId = photoId;
        await store.put(mediaKey(productId), media);
      });
    },

    addBarcode(productId, code, source) {
      return write(async (store): Promise<AddBarcodeResult> => {
        const owner = await store.get(barcodeKey(code));
        if (typeof owner === "string" && owner !== productId) {
          // A stale index entry (the other product no longer lists it) is not a conflict.
          const other = await readMedia(store, owner);
          if (other.barcodes.some((barcode) => barcode.code === code)) return { ok: false, reason: "taken", productId: owner };
        }
        const media = await readMedia(store, productId);
        if (!media.barcodes.some((barcode) => barcode.code === code)) {
          media.barcodes.push({ code, source, addedAt: now() });
          await store.put(mediaKey(productId), media);
        }
        await store.put(barcodeKey(code), productId);
        await forget(store, code);
        return { ok: true };
      });
    },

    removeBarcode(productId, code) {
      return write(async (store) => {
        const media = await readMedia(store, productId);
        media.barcodes = media.barcodes.filter((barcode) => barcode.code !== code);
        await store.put(mediaKey(productId), media);
        if ((await store.get(barcodeKey(code))) === productId) await store.delete(barcodeKey(code));
      });
    },

    async findProductByBarcode(code) {
      try {
        const store = await kv();
        if (!store) return null;
        // Codes saved before UPC-E was expanded sit under their 8-digit key.
        for (const key of [code, ...legacyUpcECodes(code)]) {
          const owner = await store.get(barcodeKey(key));
          if (typeof owner !== "string") continue;
          const media = await readMedia(store, owner);
          if (media.barcodes.some((barcode) => barcode.code === key)) return owner;
        }
        return null;
      } catch {
        return null;
      }
    },

    setAvoid(productId, avoid) {
      return write(async (store) => {
        const media = await readMedia(store, productId);
        media.avoid = avoid ? (media.avoid ?? { since: now() }) : null;
        await store.put(mediaKey(productId), media);
      });
    },

    rememberBarcode(code, off) {
      return write(async (store) => {
        const rest = (await readRemembered(store)).filter((item) => item.code !== code);
        await store.put(rememberedKey, [{ code, rememberedAt: now(), off }, ...rest].slice(0, maxRemembered));
      });
    },

    async findRememberedBarcode(code) {
      try {
        const store = await kv();
        if (!store) return null;
        return (await readRemembered(store)).find((item) => item.code === code) ?? null;
      } catch {
        return null;
      }
    },

    async listRememberedBarcodes() {
      try {
        const store = await kv();
        return store ? await readRemembered(store) : [];
      } catch {
        return [];
      }
    },

    forgetBarcode(code) {
      return write((store) => forget(store, code));
    },

    moveProductMedia(fromProductId, toProductId) {
      return write(async (store) => {
        if (fromProductId === toProductId) return;
        const source = await readMedia(store, fromProductId);
        if (source.photos.length === 0 && source.barcodes.length === 0 && source.avoid === null) return;
        const target = await readMedia(store, toProductId);
        // Photo bytes are keyed by photo id, not product, so only the lists move.
        const photoIds = new Set(target.photos.map((photo) => photo.id));
        target.photos.push(...source.photos.filter((photo) => !photoIds.has(photo.id)));
        target.mainPhotoId ??= source.mainPhotoId ?? target.photos[0]?.id ?? null;
        const codes = new Set(target.barcodes.map((barcode) => barcode.code));
        target.barcodes.push(...source.barcodes.filter((barcode) => !codes.has(barcode.code)));
        target.avoid = target.avoid && source.avoid
          ? { since: Math.min(target.avoid.since, source.avoid.since) }
          : target.avoid ?? source.avoid;
        await store.put(mediaKey(toProductId), target);
        for (const barcode of source.barcodes) await store.put(barcodeKey(barcode.code), toProductId);
        await store.delete(mediaKey(fromProductId));
      });
    },

    async clear() {
      // Queued behind writes already running, so a write that finishes later
      // cannot bring photos back after sign-out.
      // A hung write must not block sign-out: after a short wait, clear right
      // away as well; the queued clear still runs once the queue drains.
      const queued = write((store) => store.clear()).then(() => true, () => true);
      let timer: ReturnType<typeof setTimeout> | undefined;
      const timeout = new Promise<boolean>((resolve) => { timer = setTimeout(() => resolve(false), clearWaitMs); });
      const drained = await Promise.race([queued, timeout]);
      clearTimeout(timer);
      if (!drained) {
        try {
          await (await kv())?.clear();
        } catch {
          // Nothing left to protect either way.
        }
      }
    }
  };
}

function asMedia(value: unknown): StoredMedia {
  const candidate = value && typeof value === "object" ? (value as Partial<StoredMedia>) : {};
  return {
    photos: Array.isArray(candidate.photos) ? candidate.photos.map((photo) => ({ ...photo })) : [],
    mainPhotoId: typeof candidate.mainPhotoId === "string" ? candidate.mainPhotoId : null,
    barcodes: Array.isArray(candidate.barcodes) ? candidate.barcodes.map((barcode) => ({ ...barcode })) : [],
    avoid: candidate.avoid && typeof candidate.avoid.since === "number" ? { since: candidate.avoid.since } : null
  };
}

function asBytes(value: unknown): StoredPhotoBytes | null {
  const candidate = value && typeof value === "object" ? (value as Partial<StoredPhotoBytes>) : null;
  // Duck-typed: a structured clone may come back from another realm's ArrayBuffer.
  return candidate?.bytes && typeof candidate.bytes.byteLength === "number"
    ? { bytes: candidate.bytes, type: typeof candidate.type === "string" ? candidate.type : "image/jpeg" }
    : null;
}

function isRemembered(value: unknown): value is RememberedBarcode {
  return Boolean(value) && typeof value === "object"
    && typeof (value as RememberedBarcode).code === "string"
    && typeof (value as RememberedBarcode).rememberedAt === "number";
}

function randomId(): string {
  return typeof crypto !== "undefined" && "randomUUID" in crypto
    ? crypto.randomUUID()
    : `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}

/** The store the app uses: IndexedDB on this device. */
export const deviceProductMediaStore: ProductMediaStore = createProductMediaStore(
  () => openIndexedDbKeyValue("purchase-memory-product-media", "media")
);
