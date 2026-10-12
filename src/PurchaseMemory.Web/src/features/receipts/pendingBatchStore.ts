/**
 * Keeps the photos of a "separate receipts" batch on the device until each
 * one has reached the server, so a reload or an iOS-killed PWA can offer
 * «Продолжить загрузку» instead of losing the rest of the batch.
 *
 * What already reached the server is server state (`/api/receipts/my-unsaved`);
 * this store only remembers what may not have. Every call swallows storage
 * errors: private mode, a full disk or a missing IndexedDB simply mean nothing
 * is remembered, and the capture screen works exactly as before.
 */

/** One photo of an interrupted batch as the device remembers it. */
export interface StoredBatchPhoto {
  name: string;
  type: string;
  size: number;
  lastModified: number;
  /** The server answered for this photo: a receipt, or a refusal that resending cannot fix. */
  sent: boolean;
  /** The photo bytes are on the device (false: over the size cap or storage refused). */
  kept: boolean;
}

export interface StoredBatch {
  /** Who started the batch; another signed-in user never sees it. */
  userId: string | null;
  startedAt: number;
  photos: StoredBatchPhoto[];
}

export interface RestoredBatch {
  batch: StoredBatch;
  /** Photos that may still need uploading and whose bytes were kept. */
  files: File[];
  /** Photos that may still need uploading but must be picked again. */
  lost: StoredBatchPhoto[];
}

export interface PendingBatchStore {
  /** Replaces any earlier batch with these photos. Resolves once they are stored (or storing failed). */
  begin: (files: File[], userId: string | null) => Promise<void>;
  /**
   * The photo at this position no longer needs the device's copy: the server
   * has it, or refused it for good (unsupported, too large), so offering it
   * again after a reload could only fail the same way.
   */
  markDone: (index: number) => Promise<void>;
  /** The unfinished batch of this user, or null when there is nothing to resume. */
  load: (userId: string | null) => Promise<RestoredBatch | null>;
  clear: () => Promise<void>;
}

/**
 * Ten iPhone photos are usually 20–50 MB. iOS gives a home-screen web app far
 * more than this, but a cap keeps one batch from crowding out everything else
 * and from being the reason the browser evicts the app's storage.
 */
export const pendingBatchLimits = {
  maxKeptBytes: 100 * 1024 * 1024,
  /** Same window as the server's «Недавние загрузки». */
  maxAgeMs: 24 * 60 * 60 * 1000
} as const;

/** The minimal key-value surface the store needs; IndexedDB in the app, a Map in tests. */
export interface BatchKeyValue {
  get: (key: string) => Promise<unknown>;
  put: (key: string, value: unknown) => Promise<void>;
  delete: (key: string) => Promise<void>;
  clear: () => Promise<void>;
}

interface StoredPhotoBytes {
  bytes: ArrayBuffer;
}

const metaKey = "batch";
const photoKey = (index: number) => `photo-${index}`;

export function createPendingBatchStore(openStorage: () => Promise<BatchKeyValue | null>, now: () => number = Date.now): PendingBatchStore {
  let storage: Promise<BatchKeyValue | null> | null = null;
  const kv = () => {
    storage ??= openStorage().catch(() => null);
    return storage;
  };

  async function clear() {
    try {
      await (await kv())?.clear();
    } catch {
      // Nothing to resume either way.
    }
  }

  return {
    async begin(files, userId) {
      const store = await kv();
      if (!store) return;
      try {
        await store.clear();
        let budget = pendingBatchLimits.maxKeptBytes;
        const photos: StoredBatchPhoto[] = files.map((file) => {
          const kept = file.size <= budget;
          if (kept) budget -= file.size;
          return { name: file.name, type: file.type, size: file.size, lastModified: file.lastModified, sent: false, kept };
        });
        const batch: StoredBatch = { userId, startedAt: now(), photos };
        // Names first: even if no bytes fit, the user can be told what to pick again.
        await store.put(metaKey, { ...batch, photos: photos.map((photo) => ({ ...photo, kept: false })) });
        for (const [index, file] of files.entries()) {
          if (!photos[index].kept) continue;
          try {
            await store.put(photoKey(index), { bytes: await file.arrayBuffer() } satisfies StoredPhotoBytes);
          } catch {
            photos[index].kept = false;
          }
        }
        await store.put(metaKey, batch);
      } catch {
        await clear();
      }
    },

    async markDone(index) {
      const store = await kv();
      if (!store) return;
      try {
        const batch = asBatch(await store.get(metaKey));
        if (!batch?.photos[index]) return;
        batch.photos[index] = { ...batch.photos[index], sent: true };
        if (batch.photos.every((photo) => photo.sent)) {
          await store.clear();
          return;
        }
        await store.put(metaKey, batch);
        // The server has it now; free the space.
        await store.delete(photoKey(index));
      } catch {
        // A stale "not sent" only costs a duplicate check on the server later.
      }
    },

    async load(userId) {
      const store = await kv();
      if (!store) return null;
      try {
        const batch = asBatch(await store.get(metaKey));
        if (!batch) return null;
        const age = now() - batch.startedAt;
        // Any mismatch, including a batch stored without a user (or now opened
        // without one), is not this person's: never show someone else's photos.
        const foreign = batch.userId !== userId;
        if (foreign || age < 0 || age > pendingBatchLimits.maxAgeMs || batch.photos.every((photo) => photo.sent)) {
          await store.clear();
          return null;
        }
        const files: File[] = [];
        const lost: StoredBatchPhoto[] = [];
        for (const [index, photo] of batch.photos.entries()) {
          if (photo.sent) continue;
          const stored = photo.kept ? asBytes(await store.get(photoKey(index))) : null;
          if (stored) files.push(new File([stored.bytes], photo.name, { type: photo.type, lastModified: photo.lastModified }));
          else lost.push(photo);
        }
        return { batch, files, lost };
      } catch {
        return null;
      }
    },

    clear
  };
}

function asBatch(value: unknown): StoredBatch | null {
  if (!value || typeof value !== "object") return null;
  const candidate = value as Partial<StoredBatch>;
  return typeof candidate.startedAt === "number" && Array.isArray(candidate.photos)
    ? { userId: candidate.userId ?? null, startedAt: candidate.startedAt, photos: candidate.photos.map((photo) => ({ ...photo })) }
    : null;
}

function asBytes(value: unknown): StoredPhotoBytes | null {
  // Duck-typed: a structured clone may come back from another realm's ArrayBuffer.
  const bytes = value && typeof value === "object" ? (value as Partial<StoredPhotoBytes>).bytes : undefined;
  return bytes && typeof bytes.byteLength === "number" ? { bytes } : null;
}

const databaseName = "purchase-memory-pending-batch";
const objectStoreName = "batch";

/** IndexedDB behind {@link BatchKeyValue}; null when the browser has none or refuses to open it. */
export function openIndexedDbStorage(): Promise<BatchKeyValue | null> {
  return new Promise((resolve) => {
    let request: IDBOpenDBRequest;
    try {
      if (typeof indexedDB === "undefined") {
        resolve(null);
        return;
      }
      request = indexedDB.open(databaseName, 1);
    } catch {
      resolve(null);
      return;
    }
    request.onupgradeneeded = () => request.result.createObjectStore(objectStoreName);
    request.onerror = () => resolve(null);
    request.onblocked = () => resolve(null);
    request.onsuccess = () => {
      const database = request.result;
      const run = <T>(mode: IDBTransactionMode, action: (store: IDBObjectStore) => IDBRequest<T>) =>
        new Promise<T>((done, fail) => {
          const transaction = database.transaction(objectStoreName, mode);
          const operation = action(transaction.objectStore(objectStoreName));
          transaction.oncomplete = () => done(operation.result);
          transaction.onerror = () => fail(transaction.error ?? operation.error);
          transaction.onabort = () => fail(transaction.error ?? new Error("IndexedDB transaction aborted."));
        });
      resolve({
        get: (key) => run("readonly", (store) => store.get(key)),
        put: async (key, value) => { await run("readwrite", (store) => store.put(value, key)); },
        delete: async (key) => { await run("readwrite", (store) => store.delete(key)); },
        clear: async () => { await run("readwrite", (store) => store.clear()); }
      });
    };
  });
}

/** In-memory storage for tests and stories: survives a remount the way IndexedDB survives a reload. */
export function createMemoryStorage(): BatchKeyValue {
  const values = new Map<string, unknown>();
  return {
    get: async (key) => values.get(key),
    put: async (key, value) => { values.set(key, value); },
    delete: async (key) => { values.delete(key); },
    clear: async () => { values.clear(); }
  };
}

export const deviceBatchStore: PendingBatchStore = createPendingBatchStore(openIndexedDbStorage);
