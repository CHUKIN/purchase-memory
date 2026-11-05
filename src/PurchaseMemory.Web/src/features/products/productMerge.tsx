import { IonAlert } from "@ionic/react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { confirmDistinctProduct, mergeProduct } from "../../api/client";
import { brandsQueryKey, categoriesQueryKey } from "../categories/categoryQueries";
import { productMediaQueryKey } from "../product-scan/useProductMedia";
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
    queryClient.invalidateQueries({ queryKey: brandsQueryKey }),
    // Photos, barcodes and «Больше не покупать» move with the merge on the server.
    queryClient.invalidateQueries({ queryKey: productMediaQueryKey })
  ]);
}

/**
 * Merges `source` into `target` on the server; its photos, barcodes and
 * «Больше не покупать» go along there too.
 */
export function useMergeProduct() {
  const refresh = useRefreshAfterProductChange();
  return useMutation({
    mutationFn: ({ source, target }: MergeRequest) => mergeProduct(source.id, target.id),
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
