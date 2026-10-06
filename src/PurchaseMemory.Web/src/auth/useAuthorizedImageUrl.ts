import { useEffect, useState } from "react";
import { fetchProtectedBlob } from "../api/client";

/**
 * `<img src>` cannot send an Authorization header, so protected receipt
 * photos are fetched with the token and shown through a short-lived blob URL.
 * Non-API URLs (blob:, data:, stories) pass through unchanged.
 */
export function useAuthorizedImageUrl(url: string | null | undefined): { src: string | null; failed: boolean } {
  const needsFetch = Boolean(url && url.startsWith("/api/"));
  const [state, setState] = useState<{ url: string | null; src: string | null; failed: boolean }>({ url: null, src: null, failed: false });

  useEffect(() => {
    if (!url || !needsFetch) return;
    const controller = new AbortController();
    let objectUrl: string | null = null;
    fetchProtectedBlob(url, controller.signal)
      .then((blob) => {
        objectUrl = URL.createObjectURL(blob);
        setState({ url, src: objectUrl, failed: false });
      })
      .catch(() => {
        if (!controller.signal.aborted) setState({ url, src: null, failed: true });
      });
    return () => {
      controller.abort();
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [url, needsFetch]);

  if (!url) return { src: null, failed: false };
  if (!needsFetch) return { src: url, failed: false };
  return state.url === url ? { src: state.src, failed: state.failed } : { src: null, failed: false };
}
