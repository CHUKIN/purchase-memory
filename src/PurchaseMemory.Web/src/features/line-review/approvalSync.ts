import { ApiError, postLineApprovals } from "../../api/client";
import type { LineApprovalChange, LineApprovalResult } from "../../api/types";

/**
 * The swipe review's «checked» marks live on the phone first, so a swipe is
 * instant and survives a reload or a lost connection, and reach the server in
 * batches: a few seconds after the last swipe, as soon as enough have piled
 * up, and when the app is hidden or closed. A failed batch is retried with a
 * growing pause. The server answers every entry on its own and repeating a
 * batch changes nothing, so a retry is always safe.
 */

/** A mark as the phone keeps it. */
export interface LocalApproval extends LineApprovalChange {
  /**
   * `pending`: not sent yet (or must be sent again). `sending`: in a batch
   * that has not answered. `synced`: the server has it; kept briefly so the
   * screen does not flicker back before the purchase is loaded again.
   */
  state: "pending" | "sending" | "synced";
  /** When the server confirmed it (ms), for `synced`. */
  syncedAt?: number;
}

export type ApprovalSender = (items: LineApprovalChange[], options: { keepalive: boolean }) => Promise<{ items: LineApprovalResult[] }>;

export interface ApprovalSyncOptions {
  /** Separates people sharing one phone; part of the storage key. */
  userKey: string;
  send?: ApprovalSender;
  /** `null`: memory only (storage blocked). Defaults to `localStorage`. */
  storage?: Pick<Storage, "getItem" | "setItem" | "removeItem"> | null;
  /** Quiet time after the last swipe before a batch goes out. */
  debounceMs?: number;
  /** This many unsent marks go out at once, without waiting. */
  maxPending?: number;
  /** First retry pause; it doubles up to `maxBackoffMs`. */
  backoffMs?: number;
  maxBackoffMs?: number;
  /** How long a confirmed mark may override the server's answer. */
  syncedTtlMs?: number;
  now?: () => number;
}

/** The server's batch limits (`PurchaseLineApprovalService`). */
export const maxBatchItems = 200;
export const maxBatchPurchases = 20;

export const approvalStoragePrefix = "purchase-memory.line-approvals.v1:";

/** Called after a batch the server accepted, with the purchases it touched. */
export type SyncedListener = (purchaseIds: string[]) => void;

export class LineApprovalSync {
  private readonly entries = new Map<string, LocalApproval>();
  private readonly listeners = new Set<() => void>();
  private readonly syncedListeners = new Set<SyncedListener>();
  private readonly send: ApprovalSender;
  private readonly storage: Pick<Storage, "getItem" | "setItem" | "removeItem"> | null;
  private readonly storageKey: string;
  private readonly debounceMs: number;
  private readonly maxPending: number;
  private readonly backoffMs: number;
  private readonly maxBackoffMs: number;
  private readonly syncedTtlMs: number;
  private readonly now: () => number;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private inFlight: Promise<void> | null = null;
  private failures = 0;
  private retryAt = 0;
  /**
   * The server refused the session (401/403): no automatic retries until the
   * person does something again (a swipe or an explicit flush). The marks
   * stay stored for this user and only go out under this user's sync.
   */
  private authPaused = false;
  private version = 0;
  private detach: (() => void) | null = null;
  private disposed = false;

  constructor(options: ApprovalSyncOptions) {
    this.send = options.send ?? ((items, { keepalive }) => postLineApprovals(items, { keepalive }));
    this.storage = options.storage === undefined ? safeLocalStorage() : options.storage;
    this.storageKey = approvalStoragePrefix + options.userKey;
    this.debounceMs = options.debounceMs ?? 5_000;
    this.maxPending = options.maxPending ?? 20;
    this.backoffMs = options.backoffMs ?? 2_000;
    this.maxBackoffMs = options.maxBackoffMs ?? 60_000;
    this.syncedTtlMs = options.syncedTtlMs ?? 10 * 60_000;
    this.now = options.now ?? (() => Date.now());
    this.load();
  }

  /** Listens for the page being hidden or closed and for the network coming back. */
  start() {
    if (this.detach || typeof window === "undefined") return;
    const onHidden = () => { if (document.visibilityState === "hidden") void this.flush({ keepalive: true }); };
    const onPageHide = () => void this.flush({ keepalive: true });
    const onOnline = () => void this.flush();
    document.addEventListener("visibilitychange", onHidden);
    window.addEventListener("pagehide", onPageHide);
    window.addEventListener("online", onOnline);
    this.detach = () => {
      document.removeEventListener("visibilitychange", onHidden);
      window.removeEventListener("pagehide", onPageHide);
      window.removeEventListener("online", onOnline);
    };
    // Marks left from the last visit go out soon.
    if (this.pendingCount() > 0) this.schedule();
  }

  /** Stops timers and listeners (sign-out, tests). Stored marks stay. */
  dispose() {
    this.disposed = true;
    this.detach?.();
    this.detach = null;
    if (this.timer) clearTimeout(this.timer);
    this.timer = null;
  }

  subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => { this.listeners.delete(listener); };
  };

  onSynced(listener: SyncedListener) {
    this.syncedListeners.add(listener);
    return () => { this.syncedListeners.delete(listener); };
  }

  /** Changes whenever any mark changes; a snapshot for `useSyncExternalStore`. */
  getVersion = () => this.version;

  get(lineId: string): LocalApproval | undefined {
    return this.entries.get(lineId);
  }

  pendingCount() {
    let count = 0;
    for (const entry of this.entries.values()) if (entry.state !== "synced") count += 1;
    return count;
  }

  /** Records a swipe; the server hears about it in the next batch. */
  set(purchaseId: string, lineId: string, approved: boolean) {
    this.authPaused = false;
    this.entries.set(lineId, { purchaseId, lineId, approved, at: new Date(this.now()).toISOString(), state: "pending" });
    this.changed();
    if (this.pendingCount() >= this.maxPending && this.now() >= this.retryAt) void this.flush();
    else this.schedule();
  }

  /**
   * Fresh server data arrived for a purchase: confirmed marks the server now
   * agrees with (or that are old, or whose line is gone) step aside.
   */
  reconcile(purchaseId: string, serverLines: { id: string; approved?: boolean }[]) {
    const server = new Map(serverLines.map((line) => [line.id, line.approved === true]));
    let dirty = false;
    for (const [lineId, entry] of this.entries) {
      if (entry.purchaseId !== purchaseId || entry.state !== "synced") continue;
      const agrees = server.get(lineId) === entry.approved;
      const gone = !server.has(lineId);
      const old = this.now() - (entry.syncedAt ?? 0) > this.syncedTtlMs;
      if (agrees || gone || old) {
        this.entries.delete(lineId);
        dirty = true;
      }
    }
    if (dirty) this.changed();
  }

  /**
   * Sends every unsent mark now (in as many batches as the limits need).
   * Resolves when done; never rejects — a failure schedules a retry.
   */
  flush(options: { keepalive?: boolean } = {}): Promise<void> {
    if (this.disposed) return Promise.resolve();
    if (this.timer) { clearTimeout(this.timer); this.timer = null; }
    if (this.inFlight) {
      // One batch at a time; whatever is still unsent goes right after.
      return this.inFlight.then(() => this.continueFlush(options));
    }
    const batch = this.nextBatch();
    if (batch.length === 0) return Promise.resolve();
    this.inFlight = this.sendBatch(batch, options.keepalive ?? false).finally(() => { this.inFlight = null; });
    return this.inFlight.then(() => this.continueFlush(options));
  }

  /** After a successful batch, the rest goes too; after a failure, the retry timer takes over. */
  private continueFlush(options: { keepalive?: boolean }): Promise<void> | undefined {
    return this.failures === 0 && !this.authPaused && this.nextBatchSize() > 0 ? this.flush(options) : undefined;
  }

  private nextBatchSize() {
    let count = 0;
    for (const entry of this.entries.values()) if (entry.state === "pending") count += 1;
    return count;
  }

  private nextBatch(): LocalApproval[] {
    const batch: LocalApproval[] = [];
    const purchases = new Set<string>();
    for (const entry of this.entries.values()) {
      if (entry.state !== "pending") continue;
      if (!purchases.has(entry.purchaseId) && purchases.size >= maxBatchPurchases) continue;
      purchases.add(entry.purchaseId);
      batch.push(entry);
      if (batch.length >= maxBatchItems) break;
    }
    return batch;
  }

  private async sendBatch(batch: LocalApproval[], keepalive: boolean) {
    for (const entry of batch) entry.state = "sending";
    const sent = batch.map(({ purchaseId, lineId, approved, at }) => ({ purchaseId, lineId, approved, at }));
    try {
      const response = await this.send(sent, { keepalive });
      const results = new Map(response.items.map((result) => [result.lineId, result]));
      const touched = new Set<string>();
      for (const item of sent) {
        const entry = this.entries.get(item.lineId);
        // Swiped again while the batch was out: the newer mark still has to go.
        if (!entry || entry.at !== item.at || entry.approved !== item.approved) continue;
        const result = results.get(item.lineId);
        if (!result || result.status === "notFound") {
          this.entries.delete(item.lineId);
        } else {
          // The server's answer is the truth (e.g. a stale undo that lost).
          this.entries.set(item.lineId, { ...entry, approved: result.approved, state: "synced", syncedAt: this.now() });
        }
        touched.add(item.purchaseId);
      }
      this.failures = 0;
      this.retryAt = 0;
      this.changed();
      if (touched.size > 0) this.syncedListeners.forEach((listener) => listener([...touched]));
    } catch (error) {
      for (const item of sent) {
        const entry = this.entries.get(item.lineId);
        if (entry?.state === "sending") entry.state = "pending";
      }
      // A malformed batch can never succeed; a refused session waits for the
      // person; anything else is retried with a growing pause.
      if (error instanceof ApiError && (error.status === 401 || error.status === 403)) {
        this.authPaused = true;
      } else if (error instanceof ApiError && error.status === 400) {
        for (const item of sent) {
          const entry = this.entries.get(item.lineId);
          if (entry && entry.at === item.at) this.entries.delete(item.lineId);
        }
      } else {
        this.failures += 1;
        const pause = Math.min(this.backoffMs * 2 ** (this.failures - 1), this.maxBackoffMs);
        this.retryAt = this.now() + pause;
      }
      this.changed();
      this.schedule();
    }
  }

  private schedule() {
    if (this.timer) clearTimeout(this.timer);
    this.timer = null;
    if (this.disposed || this.authPaused || this.nextBatchSize() === 0) return;
    const wait = Math.max(this.debounceMs, this.retryAt - this.now());
    this.timer = setTimeout(() => { this.timer = null; void this.flush(); }, wait);
  }

  private changed() {
    this.version += 1;
    this.save();
    this.listeners.forEach((listener) => listener());
  }

  private load() {
    if (!this.storage) return;
    try {
      const raw = this.storage.getItem(this.storageKey);
      if (!raw) return;
      const stored = JSON.parse(raw) as unknown;
      if (!Array.isArray(stored)) return;
      for (const value of stored) {
        if (!isLocalApproval(value)) continue;
        // A batch cut off by a reload never answered: send it again.
        this.entries.set(value.lineId, value.state === "sending" ? { ...value, state: "pending" } : value);
      }
    } catch {
      // Unreadable or blocked storage: start empty.
    }
  }

  private save() {
    if (!this.storage) return;
    try {
      if (this.entries.size === 0) this.storage.removeItem(this.storageKey);
      else this.storage.setItem(this.storageKey, JSON.stringify([...this.entries.values()]));
    } catch {
      // Storage full or blocked: the marks still live in memory for this visit.
    }
  }
}

function isLocalApproval(value: unknown): value is LocalApproval {
  if (typeof value !== "object" || value === null) return false;
  const entry = value as Record<string, unknown>;
  return typeof entry.purchaseId === "string"
    && typeof entry.lineId === "string"
    && typeof entry.approved === "boolean"
    && typeof entry.at === "string"
    && (entry.state === "pending" || entry.state === "sending" || entry.state === "synced");
}

function safeLocalStorage() {
  try {
    return typeof window === "undefined" ? null : window.localStorage;
  } catch {
    return null;
  }
}

const syncs = new Map<string, LineApprovalSync>();

/**
 * The signed-in person's sync, started on first use. Only one person's sync
 * runs at a time: another person's (or a signed-out session's) stops, so its
 * marks are never sent with someone else's token. They stay stored and go out
 * when that person signs in again.
 */
export function approvalSyncFor(userKey: string): LineApprovalSync {
  releaseApprovalSyncs(userKey);
  let sync = syncs.get(userKey);
  if (!sync) {
    sync = new LineApprovalSync({ userKey });
    sync.start();
    syncs.set(userKey, sync);
  }
  return sync;
}

/** Stops every sync except `keep`'s (sign-out: `null` stops all). */
export function releaseApprovalSyncs(keep: string | null) {
  for (const [key, sync] of syncs) {
    if (key === keep) continue;
    sync.dispose();
    syncs.delete(key);
  }
}

/** Tests: forget every sync (timers and listeners stop). */
export function resetApprovalSyncs() {
  releaseApprovalSyncs(null);
}
