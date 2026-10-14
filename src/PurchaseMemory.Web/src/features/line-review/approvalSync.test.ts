import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "../../api/client";
import type { LineApprovalChange, LineApprovalResult } from "../../api/types";
import { approvalStoragePrefix, approvalSyncFor, LineApprovalSync, releaseApprovalSyncs, resetApprovalSyncs, type ApprovalSender } from "./approvalSync";

/** A server that applies everything (or answers `notFound` for unknown lines). */
function okSender(unknown: string[] = []) {
  return vi.fn<ApprovalSender>(async (items: LineApprovalChange[]) => ({
    items: items.map((item): LineApprovalResult => unknown.includes(item.lineId)
      ? { purchaseId: item.purchaseId, lineId: item.lineId, status: "notFound", approved: false, approvedAt: null }
      : { purchaseId: item.purchaseId, lineId: item.lineId, status: "applied", approved: item.approved, approvedAt: item.at })
  }));
}

const memoryStorage = () => {
  const values = new Map<string, string>();
  return {
    getItem: (key: string) => values.get(key) ?? null,
    setItem: (key: string, value: string) => { values.set(key, value); },
    removeItem: (key: string) => { values.delete(key); },
    values
  };
};

describe("LineApprovalSync", () => {
  let syncs: LineApprovalSync[] = [];
  const create = (options: Partial<ConstructorParameters<typeof LineApprovalSync>[0]> = {}) => {
    const sync = new LineApprovalSync({ userKey: "anna", storage: null, ...options });
    syncs.push(sync);
    return sync;
  };

  beforeEach(() => { vi.useFakeTimers(); });
  afterEach(() => {
    syncs.forEach((sync) => sync.dispose());
    syncs = [];
    vi.useRealTimers();
  });

  it("keeps a swipe at once and sends one batch a few seconds after the last swipe", async () => {
    const send = okSender();
    const sync = create({ send, debounceMs: 5_000 });

    sync.set("p1", "a", true);
    expect(sync.get("a")).toMatchObject({ approved: true, state: "pending" });
    await vi.advanceTimersByTimeAsync(3_000);
    sync.set("p1", "b", true);
    await vi.advanceTimersByTimeAsync(4_000);
    expect(send).not.toHaveBeenCalled();

    await vi.advanceTimersByTimeAsync(1_000);
    expect(send).toHaveBeenCalledTimes(1);
    expect(send.mock.calls[0][0].map((item) => item.lineId)).toEqual(["a", "b"]);
    expect(send.mock.calls[0][1]).toEqual({ keepalive: false });
    expect(sync.get("a")?.state).toBe("synced");
    expect(sync.pendingCount()).toBe(0);
  });

  it("sends at once when enough swipes pile up", async () => {
    const send = okSender();
    const sync = create({ send, maxPending: 3 });
    sync.set("p1", "a", true);
    sync.set("p1", "b", true);
    expect(send).not.toHaveBeenCalled();
    sync.set("p1", "c", true);
    await vi.advanceTimersByTimeAsync(0);
    expect(send).toHaveBeenCalledTimes(1);
    expect(send.mock.calls[0][0]).toHaveLength(3);
  });

  it("flushes with keepalive when the page is hidden or closed", async () => {
    const send = okSender();
    const sync = create({ send });
    sync.start();
    sync.set("p1", "a", true);

    Object.defineProperty(document, "visibilityState", { value: "hidden", configurable: true });
    document.dispatchEvent(new Event("visibilitychange"));
    await vi.advanceTimersByTimeAsync(0);
    expect(send).toHaveBeenCalledWith([expect.objectContaining({ lineId: "a" })], { keepalive: true });

    sync.set("p1", "b", false);
    window.dispatchEvent(new Event("pagehide"));
    await vi.advanceTimersByTimeAsync(0);
    expect(send).toHaveBeenCalledTimes(2);
    expect(send.mock.calls[1][1]).toEqual({ keepalive: true });
    Object.defineProperty(document, "visibilityState", { value: "visible", configurable: true });
  });

  it("retries a failed batch with a growing pause and keeps the marks meanwhile", async () => {
    const send = vi.fn<ApprovalSender>()
      .mockRejectedValueOnce(new TypeError("Failed to fetch"))
      .mockRejectedValueOnce(new ApiError("down", 503));
    const ok = okSender();
    send.mockImplementation(ok);
    const sync = create({ send, debounceMs: 1_000, backoffMs: 4_000, maxBackoffMs: 60_000 });

    sync.set("p1", "a", true);
    await vi.advanceTimersByTimeAsync(1_000);
    expect(send).toHaveBeenCalledTimes(1);
    expect(sync.get("a")).toMatchObject({ approved: true, state: "pending" });

    await vi.advanceTimersByTimeAsync(3_900);
    expect(send).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(100);
    expect(send).toHaveBeenCalledTimes(2);

    // Second failure: the pause doubles.
    await vi.advanceTimersByTimeAsync(7_900);
    expect(send).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(100);
    expect(send).toHaveBeenCalledTimes(3);
    expect(sync.get("a")?.state).toBe("synced");
  });

  it("drops entries the server does not know and a malformed batch, but not a newer swipe", async () => {
    const send = okSender(["gone"]);
    const sync = create({ send });
    sync.set("p1", "gone", true);
    sync.set("p1", "a", true);
    await sync.flush();
    expect(sync.get("gone")).toBeUndefined();
    expect(sync.get("a")?.state).toBe("synced");

    const reject = vi.fn<ApprovalSender>().mockRejectedValue(new ApiError("bad", 400));
    const strict = create({ send: reject });
    strict.set("p1", "x", true);
    await strict.flush();
    expect(strict.get("x")).toBeUndefined();
  });

  it("a swipe made while its batch is out is sent again", async () => {
    let answer: () => void = () => undefined;
    const send = vi.fn<ApprovalSender>((items) => new Promise((resolve) => {
      answer = () => resolve({ items: items.map((item) => ({ ...item, status: "applied", approvedAt: item.at })) });
    }));
    const sync = create({ send });
    sync.set("p1", "a", true);
    const flushing = sync.flush();
    vi.setSystemTime(Date.now() + 1_000);
    sync.set("p1", "a", false);
    answer();
    await vi.advanceTimersByTimeAsync(0);
    // The first answer did not overwrite the newer swipe; it goes out right away.
    expect(send).toHaveBeenCalledTimes(2);
    expect(send.mock.calls[1][0]).toEqual([expect.objectContaining({ lineId: "a", approved: false })]);
    answer();
    await flushing;
    expect(sync.get("a")).toMatchObject({ approved: false, state: "synced" });
  });

  it("survives a reload: unsent and cut-off marks come back as pending", () => {
    const storage = memoryStorage();
    const first = create({ storage, send: okSender() });
    first.set("p1", "a", true);
    first.set("p1", "b", true);
    const stored = JSON.parse(storage.values.get(`${approvalStoragePrefix}anna`)!) as { lineId: string; state: string }[];
    stored[1].state = "sending";
    storage.setItem(`${approvalStoragePrefix}anna`, JSON.stringify(stored));

    const reloaded = create({ storage, send: okSender() });
    expect(reloaded.get("a")).toMatchObject({ approved: true, state: "pending" });
    expect(reloaded.get("b")).toMatchObject({ approved: true, state: "pending" });
    // Another person on the same phone has their own marks.
    expect(create({ storage, userKey: "boris" }).get("a")).toBeUndefined();
  });

  it("merges with the server: a confirmed mark steps aside once the server agrees or the line is gone", async () => {
    const sync = create({ send: okSender(), syncedTtlMs: 60_000 });
    sync.set("p1", "a", true);
    sync.set("p1", "b", true);
    sync.set("p1", "c", true);
    sync.set("p2", "z", true);
    await sync.flush();

    // Stale purchase data (before the reload): the phone's marks still win.
    sync.reconcile("p1", [{ id: "a", approved: false }, { id: "b", approved: false }, { id: "c", approved: false }]);
    expect(sync.get("a")?.approved).toBe(true);

    // Fresh data: «a» agrees, «b» was replaced by a correction, «c» still differs.
    sync.reconcile("p1", [{ id: "a", approved: true }, { id: "c", approved: false }]);
    expect(sync.get("a")).toBeUndefined();
    expect(sync.get("b")).toBeUndefined();
    expect(sync.get("c")?.approved).toBe(true);
    // Another purchase is not touched.
    expect(sync.get("z")).toBeDefined();

    // After a while the server's answer wins (someone else unchecked it).
    vi.setSystemTime(Date.now() + 61_000);
    sync.reconcile("p1", [{ id: "c", approved: false }]);
    expect(sync.get("c")).toBeUndefined();
  });

  it("an unsent mark always wins over the server", () => {
    const sync = create({ send: okSender() });
    sync.set("p1", "a", false);
    sync.reconcile("p1", [{ id: "a", approved: true }]);
    expect(sync.get("a")).toMatchObject({ approved: false, state: "pending" });
  });

  it("splits a big queue into batches within the server limits", async () => {
    const send = okSender();
    const sync = create({ send, maxPending: 10_000 });
    for (let index = 0; index < 250; index += 1) sync.set(`p${index % 25}`, `line-${index}`, true);
    await sync.flush();
    expect(send.mock.calls.length).toBeGreaterThanOrEqual(2);
    for (const [items] of send.mock.calls) {
      expect(items.length).toBeLessThanOrEqual(200);
      expect(new Set(items.map((item) => item.purchaseId)).size).toBeLessThanOrEqual(20);
    }
    expect(sync.pendingCount()).toBe(0);
  });

  it("stops retrying when the session is refused and resumes on the next swipe", async () => {
    const send = vi.fn<ApprovalSender>().mockRejectedValueOnce(new ApiError("expired", 401)).mockImplementation(okSender());
    const sync = create({ send, debounceMs: 1_000, backoffMs: 1_000 });
    sync.set("p1", "a", true);
    await vi.advanceTimersByTimeAsync(1_000);
    expect(send).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(10 * 60_000);
    expect(send).toHaveBeenCalledTimes(1);
    expect(sync.get("a")?.state).toBe("pending");

    sync.set("p1", "b", true);
    await vi.advanceTimersByTimeAsync(1_000);
    expect(send).toHaveBeenCalledTimes(2);
    expect(send.mock.calls[1][0].map((item) => item.lineId)).toEqual(["a", "b"]);
  });
});

describe("approvalSyncFor", () => {
  afterEach(() => resetApprovalSyncs());

  it("runs one person's sync at a time and stops it on sign-out", async () => {
    const anna = approvalSyncFor("anna");
    expect(approvalSyncFor("anna")).toBe(anna);
    const dispose = vi.spyOn(anna, "dispose");
    const boris = approvalSyncFor("boris");
    expect(dispose).toHaveBeenCalled();
    // A stopped sync never sends again (Boris's token must not carry Anna's marks).
    await expect(anna.flush()).resolves.toBeUndefined();
    const borisDispose = vi.spyOn(boris, "dispose");
    releaseApprovalSyncs(null);
    expect(borisDispose).toHaveBeenCalled();
    expect(approvalSyncFor("boris")).not.toBe(boris);
  });
});
