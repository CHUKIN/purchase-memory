import { useQuery } from "@tanstack/react-query";
import { getReviewQueueCount } from "../../api/client";
import type { ReviewQueueSummary } from "../../api/types";
import { useRememberedBarcodes } from "../product-scan/useProductMedia";
import { reviewQueueCountQueryKey } from "./reviewQueueQueries";

export interface ReviewCount {
  /** Everything waiting in «Ревью»; null while unknown. */
  total: number | null;
  /** The server's part, by section. */
  summary: ReviewQueueSummary | null;
  /** Codes remembered on this device, not linked to a product yet. */
  barcodes: number;
}

/**
 * The one number of «Ревью»: the server's summary plus this device's
 * remembered barcodes. The tab badge, the Home card and the hub all read it,
 * so they always agree. Refreshes after queue actions, when a receipt
 * finishes (both invalidate `reviewQueueQueryKey`) and once a minute.
 */
export function useReviewCount(): ReviewCount {
  const count = useQuery({ queryKey: reviewQueueCountQueryKey, queryFn: getReviewQueueCount, retry: false, staleTime: 30_000, refetchInterval: 60_000 });
  const remembered = useRememberedBarcodes();
  const barcodes = remembered.data?.length ?? 0;
  const server = count.data ? count.data.summary?.totalCount ?? count.data.totalCount : null;
  return {
    total: server === null ? null : server + barcodes,
    summary: count.data?.summary ?? null,
    barcodes
  };
}
