import { useCallback, useEffect, useRef, useState } from "react";
import { ApiError, uploadReceipt } from "../../api/client";
import type { PendingBatchStore } from "./pendingBatchStore";

/** Where one photo of a "separate receipts" batch stands. */
export type BatchPhotoState =
  | { status: "waiting" }
  | { status: "uploading" }
  | { status: "uploaded"; receiptId: string }
  | { status: "duplicate"; receiptId: string }
  | { status: "failed"; message: string | null }
  | { status: "rateLimited"; retryAfterSeconds: number | null };

export interface ReceiptBatchUpload {
  /** One state per selected photo once a batch has started, otherwise null. */
  photos: BatchPhotoState[] | null;
  running: boolean;
  /** Uploads every photo that is not uploaded yet, one request at a time. */
  start: (files: File[]) => void;
  /** Uploads one photo again (after a failure or a rate-limit stop). */
  retry: (files: File[], index: number) => void;
  reset: () => void;
}

/** Where the photos of a batch wait until the server has them, and whose batch it is. */
export interface BatchPersistence {
  store: PendingBatchStore;
  userId: string | null;
}

/**
 * Uploads each photo as its own receipt, strictly one after another.
 *
 * Every upload asks the server to queue recognition in the same request, so a
 * photo costs one rate-limit token. A 429 stops the batch instead of firing
 * the remaining photos into a limit that will only refuse them again; the
 * user resumes it deliberately once the wait is over.
 *
 * With `persistence`, a new batch is written to the device before the first
 * upload and each photo is forgotten once the server has it, so a reload can
 * offer the rest again. Re-sending a photo whose answer was lost is safe: the
 * server recognises the same bytes as an exact duplicate.
 */
export function useReceiptBatchUpload(onUploaded?: () => void, persistence?: BatchPersistence): ReceiptBatchUpload {
  const [photos, setPhotos] = useState<BatchPhotoState[] | null>(null);
  const [running, setRunning] = useState(false);
  const photosRef = useRef<BatchPhotoState[] | null>(null);
  const runningRef = useRef(false);
  const mountedRef = useRef(true);
  const onUploadedRef = useRef(onUploaded);
  useEffect(() => { onUploadedRef.current = onUploaded; }, [onUploaded]);
  const persistenceRef = useRef(persistence);
  useEffect(() => { persistenceRef.current = persistence; }, [persistence]);

  useEffect(() => {
    mountedRef.current = true;
    return () => { mountedRef.current = false; };
  }, []);

  const write = useCallback((next: BatchPhotoState[]) => {
    photosRef.current = next;
    setPhotos(next);
  }, []);
  const update = useCallback((index: number, state: BatchPhotoState) => {
    const current = photosRef.current;
    if (!current) return;
    write(current.map((photo, position) => position === index ? state : photo));
  }, [write]);

  const run = useCallback(async (files: File[], indices: number[], isNewBatch = false) => {
    if (runningRef.current) return;
    runningRef.current = true;
    setRunning(true);
    try {
      const store = persistenceRef.current?.store;
      // Stored before anything is sent, so a reload right after the first upload still has the rest.
      if (isNewBatch) await store?.begin(files, persistenceRef.current?.userId ?? null);
      for (const index of indices) {
        // Leaving the screen stops the batch; the photo already in flight finishes.
        if (!mountedRef.current) return;
        update(index, { status: "uploading" });
        try {
          const result = await uploadReceipt([files[index]], { process: true });
          update(index, result.exactDuplicate
            ? { status: "duplicate", receiptId: result.receipt.id }
            : { status: "uploaded", receiptId: result.receipt.id });
          onUploadedRef.current?.();
          await store?.markDone(index);
        } catch (error) {
          if (error instanceof ApiError && error.status === 429) {
            update(index, { status: "rateLimited", retryAfterSeconds: error.retryAfterSeconds });
            return;
          }
          update(index, { status: "failed", message: error instanceof Error && error.message ? error.message : null });
          // The server rejected this photo itself (unsupported, too large, empty):
          // the same bytes would be refused again, so a reload must not offer it.
          // Network errors, 5xx and auth problems stay resumable.
          if (isFinalRejection(error)) await store?.markDone(index);
        }
      }
    } finally {
      runningRef.current = false;
      if (mountedRef.current) setRunning(false);
    }
  }, [update]);

  const start = useCallback((files: File[]) => {
    if (runningRef.current) return;
    const continuing = photosRef.current?.length === files.length;
    const current = continuing && photosRef.current ? photosRef.current : files.map((): BatchPhotoState => ({ status: "waiting" }));
    const pending = current.flatMap((photo, index) => photo.status === "uploaded" || photo.status === "duplicate" ? [] : [index]);
    write(current.map((photo, index) => pending.includes(index) ? { status: "waiting" } : photo));
    void run(files, pending, !continuing);
  }, [run, write]);

  const retry = useCallback((files: File[], index: number) => {
    if (runningRef.current || !photosRef.current) return;
    void run(files, [index]);
  }, [run]);

  const reset = useCallback(() => {
    if (runningRef.current) return;
    // Only a batch this screen started is dropped; picking new photos must not
    // silently discard an interrupted batch that is still waiting to be resumed.
    if (photosRef.current) void persistenceRef.current?.store.clear();
    photosRef.current = null;
    setPhotos(null);
  }, []);

  return { photos, running, start, retry, reset };
}

/** A 4xx about the photo itself, not about the session, timing or the rate limit. */
function isFinalRejection(error: unknown): boolean {
  return error instanceof ApiError && error.status >= 400 && error.status < 500 && ![401, 403, 408, 429].includes(error.status);
}
