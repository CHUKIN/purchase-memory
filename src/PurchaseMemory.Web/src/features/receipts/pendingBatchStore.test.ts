import "fake-indexeddb/auto";
import { afterEach, describe, expect, it } from "vitest";
import { createMemoryStorage, createPendingBatchStore, openIndexedDbStorage, pendingBatchLimits, type BatchKeyValue } from "./pendingBatchStore";

const photo = (name: string, content = name, size?: number) => {
  const file = new File([content], name, { type: "image/jpeg", lastModified: 1_700_000_000_000 });
  if (size !== undefined) Object.defineProperty(file, "size", { value: size });
  return file;
};

async function textOf(file: File) {
  return new TextDecoder().decode(await file.arrayBuffer());
}

describe("pending batch store (IndexedDB)", () => {
  afterEach(async () => {
    await (await openIndexedDbStorage())?.clear();
  });

  it("gives back the very same bytes of every photo not sent yet, after a fresh open", async () => {
    const store = createPendingBatchStore(openIndexedDbStorage);
    await store.begin([photo("a.jpg"), photo("b.jpg"), photo("c.jpg")], "anna");
    await store.markDone(0);

    // A reload: a new store instance over the same database.
    const restored = await createPendingBatchStore(openIndexedDbStorage).load("anna");

    expect(restored?.files.map((file) => file.name)).toEqual(["b.jpg", "c.jpg"]);
    expect(await Promise.all(restored!.files.map(textOf))).toEqual(["b.jpg", "c.jpg"]);
    expect(restored?.files[0].type).toBe("image/jpeg");
    expect(restored?.files[0].lastModified).toBe(1_700_000_000_000);
    expect(restored?.lost).toEqual([]);
  });

  it("forgets the batch once every photo has reached the server", async () => {
    const store = createPendingBatchStore(openIndexedDbStorage);
    await store.begin([photo("a.jpg"), photo("b.jpg")], "anna");
    await store.markDone(1);
    await store.markDone(0);

    expect(await store.load("anna")).toBeNull();
  });
});

describe("pending batch store rules", () => {
  it("keeps names but not bytes beyond the size cap", async () => {
    const store = createPendingBatchStore(async () => createMemoryStorage());
    const big = pendingBatchLimits.maxKeptBytes - 10;
    await store.begin([photo("big.jpg", "big", big), photo("next.jpg", "next", 20), photo("small.jpg", "s", 5)], null);

    const restored = await store.load(null);

    expect(restored?.files.map((file) => file.name)).toEqual(["big.jpg", "small.jpg"]);
    expect(restored?.lost.map((lost) => lost.name)).toEqual(["next.jpg"]);
  });

  it("reports a photo the storage refused as one to pick again", async () => {
    const memory = createMemoryStorage();
    const refusing: BatchKeyValue = {
      ...memory,
      put: async (key, value) => {
        if (key === "photo-1") throw new DOMException("Quota exceeded", "QuotaExceededError");
        await memory.put(key, value);
      }
    };
    const store = createPendingBatchStore(async () => refusing);
    await store.begin([photo("a.jpg"), photo("b.jpg")], null);

    const restored = await store.load(null);

    expect(restored?.files.map((file) => file.name)).toEqual(["a.jpg"]);
    expect(restored?.lost.map((lost) => lost.name)).toEqual(["b.jpg"]);
  });

  it("drops a batch older than a day or started by someone else", async () => {
    let now = 1_000_000;
    const memory = createMemoryStorage();
    const store = createPendingBatchStore(async () => memory, () => now);
    await store.begin([photo("a.jpg")], "anna");

    expect(await store.load("boris")).toBeNull();
    await store.begin([photo("a.jpg")], "anna");
    now += pendingBatchLimits.maxAgeMs + 1;
    expect(await store.load("anna")).toBeNull();
    expect(await memory.get("batch")).toBeUndefined();
  });

  it("treats a missing user on either side as a different person", async () => {
    const store = createPendingBatchStore(async () => createMemoryStorage());
    await store.begin([photo("a.jpg")], null);
    expect(await store.load("anna")).toBeNull();

    await store.begin([photo("a.jpg")], "anna");
    expect(await store.load(null)).toBeNull();

    await store.begin([photo("a.jpg")], "anna");
    expect((await store.load("anna"))?.files).toHaveLength(1);
  });

  it("never throws when there is no storage at all", async () => {
    const missing = createPendingBatchStore(async () => null);
    const broken = createPendingBatchStore(async () => { throw new Error("SecurityError"); });

    for (const store of [missing, broken]) {
      await expect(store.begin([photo("a.jpg")], null)).resolves.toBeUndefined();
      await expect(store.markDone(0)).resolves.toBeUndefined();
      await expect(store.load(null)).resolves.toBeNull();
      await expect(store.clear()).resolves.toBeUndefined();
    }
  });
});
