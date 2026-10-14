import { useQuery } from "@tanstack/react-query";
import { getReceipt } from "../../api/client";
import { useAuthorizedImageUrl } from "../../auth/useAuthorizedImageUrl";
import type { LineViewerPhoto } from "../receipt-photo/ReceiptLineViewer";

/**
 * The receipt photo behind a saved purchase, for showing its lines on it.
 *
 * Line boxes only make sense on a single-photo receipt (see `ReceiptPhoto`),
 * and the purchase does not say how many photos its receipt has, so the
 * receipt itself is asked. Until it answers, or if it cannot, nothing is
 * highlighted: the whole first photo is the safe fallback.
 */
export function usePurchaseReceiptPhoto(sourceReceiptId: string | null): { url: string | null; viewer: LineViewerPhoto | null } {
  const url = sourceReceiptId ? `/api/receipts/${sourceReceiptId}/image` : null;
  const image = useAuthorizedImageUrl(url);
  const receipt = useQuery({
    queryKey: ["receipts", sourceReceiptId],
    queryFn: () => getReceipt(sourceReceiptId!),
    enabled: sourceReceiptId !== null,
    staleTime: 60_000,
    retry: false
  });
  if (!url) return { url: null, viewer: null };
  const croppable = receipt.data !== undefined && receipt.data.imageParts.length <= 1;
  return {
    url,
    viewer: { photo: image.src ? { url: image.src, croppable } : null, failed: image.failed }
  };
}
