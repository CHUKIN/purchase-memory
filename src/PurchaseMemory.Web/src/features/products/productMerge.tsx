import { IonAlert } from "@ionic/react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { confirmDistinctProduct, mergeProduct } from "../../api/client";
import { brandsQueryKey, categoriesQueryKey } from "../categories/categoryQueries";
import { useMoveProductMedia } from "../product-scan/useProductMedia";
import { reviewQueueQueryKey } from "../review-queue/reviewQueueQueries";
import { tagsQueryKey } from "../tags/tagQueries";

/** A product named in a merge: what the confirmation says and where the screen goes after. */
export interface MergeSide {
  id: string;
  name: string;
}

export interface MergeRequest {
  source: MergeSide;
  target: MergeSide;
}

/**
 * Everything a product touched is shown somewhere else too (catalog, queue,
 * purchases, tag and category counts), so a merge or a settled flag refreshes all of it.
 */
function useRefreshAfterProductChange() {
  const queryClient = useQueryClient();
  return () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ["products"] }),
    queryClient.invalidateQueries({ queryKey: reviewQueueQueryKey }),
    queryClient.invalidateQueries({ queryKey: ["purchases"] }),
    queryClient.invalidateQueries({ queryKey: tagsQueryKey }),
    queryClient.invalidateQueries({ queryKey: categoriesQueryKey }),
    queryClient.invalidateQueries({ queryKey: brandsQueryKey })
  ]);
}

/**
 * Merges `source` into `target` on the server, then moves this device's
 * photos, barcodes and «Больше не покупать» of the source to the target
 * (they live on the phone only, so the server cannot). A device that cannot
 * keep media loses nothing that the server holds, so that step never fails the merge.
 */
export function useMergeProduct() {
  const refresh = useRefreshAfterProductChange();
  const moveMedia = useMoveProductMedia();
  return useMutation({
    mutationFn: async ({ source, target }: MergeRequest) => {
      const result = await mergeProduct(source.id, target.id);
      await moveMedia(source.id, target.id).catch(() => undefined);
      return result;
    },
    // Not awaited: the caller moves on to the target at once, so the source's
    // page never shows its own «not found» while the lists refresh.
    onSuccess: () => { void refresh(); }
  });
}

/** «Это другой товар»: the flag and the candidate list go away, the product stays. */
export function useConfirmDistinct() {
  const refresh = useRefreshAfterProductChange();
  return useMutation({
    mutationFn: (productId: string) => confirmDistinctProduct(productId),
    onSuccess: () => refresh()
  });
}

/**
 * The one question before a merge, naming both products, because a merge has
 * no undo. `request` null keeps it closed.
 */
export function MergeConfirmAlert({ request, onConfirm, onCancel }: {
  request: MergeRequest | null;
  onConfirm: (request: MergeRequest) => void;
  onCancel: () => void;
}) {
  const { t } = useTranslation();
  return (
    <IonAlert
      isOpen={request !== null}
      header={t("products.mergeConfirmTitle")}
      message={request ? t("products.mergeConfirm", { source: request.source.name, target: request.target.name }) : undefined}
      buttons={[
        { text: t("ui.cancel"), role: "cancel" },
        { text: t("products.mergeConfirmYes"), role: "destructive", handler: () => { if (request) onConfirm(request); } }
      ]}
      onDidDismiss={onCancel}
    />
  );
}
