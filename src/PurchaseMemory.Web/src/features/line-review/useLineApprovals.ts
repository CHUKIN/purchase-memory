import { useQueryClient } from "@tanstack/react-query";
import { useCallback, useEffect, useSyncExternalStore } from "react";
import type { PurchaseDetail, PurchaseDetailItem } from "../../api/types";
import { useAuth } from "../../auth/AuthContext";
import { approvalSyncFor, type LineApprovalSync } from "./approvalSync";

/** Tests and stories run without the auth gate; they share one local key. */
const anonymousKey = "local";

/** The signed-in person's mark queue. */
export function useApprovalSync(): LineApprovalSync {
  const auth = useAuth();
  return approvalSyncFor(auth?.user.id ?? anonymousKey);
}

export interface LineApprovals {
  isApproved: (item: Pick<PurchaseDetailItem, "id" | "approved">) => boolean;
  setApproved: (lineId: string, approved: boolean) => void;
  /** How many of the purchase's lines are checked. */
  approvedCount: number;
  /** Sends unsent marks now, e.g. before a correction replaces the lines. */
  flush: () => Promise<void>;
}

/**
 * A purchase's line marks: what the phone recorded wins over the server until
 * the server has it, then the server's state (which also shows the other
 * household member's marks) takes over. A confirmed batch reloads the
 * purchase so the two meet.
 */
export function useLineApprovals(purchase: PurchaseDetail, queryKey: readonly unknown[]): LineApprovals {
  const sync = useApprovalSync();
  const queryClient = useQueryClient();
  const version = useSyncExternalStore(sync.subscribe, sync.getVersion, sync.getVersion);

  useEffect(() => {
    sync.reconcile(purchase.id, purchase.items);
  }, [sync, purchase]);

  const keyText = JSON.stringify(queryKey);
  useEffect(() => sync.onSynced((purchaseIds) => {
    if (purchaseIds.includes(purchase.id)) void queryClient.invalidateQueries({ queryKey: JSON.parse(keyText) as unknown[] });
  }), [sync, queryClient, purchase.id, keyText]);

  const isApproved = useCallback(
    (item: Pick<PurchaseDetailItem, "id" | "approved">) => sync.get(item.id)?.approved ?? item.approved === true,
    // `version` changes with every mark, so memoized consumers see it too.
    [sync, version]
  );
  const setApproved = useCallback(
    (lineId: string, approved: boolean) => sync.set(purchase.id, lineId, approved),
    [sync, purchase.id]
  );
  const flush = useCallback(() => sync.flush(), [sync]);
  const approvedCount = purchase.items.filter((item) => isApproved(item)).length;
  return { isApproved, setApproved, approvedCount, flush };
}
