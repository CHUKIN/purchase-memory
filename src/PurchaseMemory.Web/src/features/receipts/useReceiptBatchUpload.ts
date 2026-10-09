import { useCallback, useEffect, useRef, useState } from "react";
import { ApiError, uploadReceipt } from "../../api/client";

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

/**
 * Uploads each photo as its own receipt, strictly one after another.
 *
 * Every upload asks the server to queue recognition in the same request, so a
 * photo costs one rate-limit token. A 429 stops the batch instead of firing
 * the remaining photos into a limit that will only refuse them again; the
 * user resumes it deliberately once the wait is over.
 */
export function useReceiptBatchUpload(onUploaded?: () => void): ReceiptBatchUpload {
  const [photos, setPhotos] = useState<BatchPhotoState[] | null>(null);
  const [running, setRunning] = useState(false);
  const photosRef = useRef<BatchPhotoState[] | null>(null);
  const runningRef = useRef(false);
  const mountedRef = useRef(true);
  const onUploadedRef = useRef(onUploaded);
  useEffect(() => { onUploadedRef.current = onUploaded; }, [onUploaded]);

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

  const run = useCallback(async (files: File[], indices: number[]) => {
    if (runningRef.current) return;
    runningRef.current = true;
    setRunning(true);
    try {
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
        } catch (error) {
          if (error instanceof ApiError && error.status === 429) {
            update(index, { status: "rateLimited", retryAfterSeconds: error.retryAfterSeconds });
            return;
          }
          update(index, { status: "failed", message: error instanceof Error && error.message ? error.message : null });
        }
      }
    } finally {
      runningRef.current = false;
      if (mountedRef.current) setRunning(false);
    }
  }, [update]);

  const start = useCallback((files: File[]) => {
    if (runningRef.current) return;
    const current = photosRef.current?.length === files.length ? photosRef.current : files.map((): BatchPhotoState => ({ status: "waiting" }));
    const pending = current.flatMap((photo, index) => photo.status === "uploaded" || photo.status === "duplicate" ? [] : [index]);
    write(current.map((photo, index) => pending.includes(index) ? { status: "waiting" } : photo));
    void run(files, pending);
  }, [run, write]);

  const retry = useCallback((files: File[], index: number) => {
    if (runningRef.current || !photosRef.current) return;
    void run(files, [index]);
  }, [run]);

  const reset = useCallback(() => {
    if (runningRef.current) return;
    photosRef.current = null;
    setPhotos(null);
  }, []);

  return { photos, running, start, retry, reset };
}
