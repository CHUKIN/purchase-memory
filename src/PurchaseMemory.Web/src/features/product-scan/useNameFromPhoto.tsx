import { IonAlert } from "@ionic/react";
import { useQueryClient } from "@tanstack/react-query";
import { useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { nameProductFromPhoto, setProductName, undoPhotoName } from "../../api/client";
import type { PhotoNameLanguageResult } from "../../api/types";
import { useToast } from "../../app/toasts";
import { reviewQueueQueryKey } from "../review-queue/reviewQueueQueries";
import { photoForNaming } from "./compressImage";

export interface NameFromPhoto {
  /**
   * Shrinks the photo (~200 KB), sends it once and reports the result.
   * `automatic` (right after a photo was added) stays silent while offline.
   */
  start: (photo: Blob, options?: { automatic?: boolean }) => Promise<void>;
  busy: boolean;
  /** The «Предложить название… Заменить?» alert; render it once. */
  dialog: ReactNode;
}

/**
 * «Название по фото» for one product. A suggested or missing name is replaced
 * at once and the toast offers «Вернуть»; a name a person set stays and the
 * photo's name is offered in an alert. Any failure is one quiet toast: the
 * photo itself is already saved on the device, nothing else depends on this.
 */
export function useNameFromPhoto(productId: string): NameFromPhoto {
  const { t } = useTranslation();
  const showToast = useToast();
  const queryClient = useQueryClient();
  const [busy, setBusy] = useState(false);
  const [proposals, setProposals] = useState<PhotoNameLanguageResult[]>([]);

  // Names show in lists, purchase lines and the «Ревью» hub too.
  const refresh = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ["products"] }),
    queryClient.invalidateQueries({ queryKey: ["purchases"] }),
    queryClient.invalidateQueries({ queryKey: reviewQueueQueryKey })
  ]);
  const toastId = (kind: string) => `name-from-photo-${kind}-${productId}-${Date.now()}`;

  async function undo(replaced: PhotoNameLanguageResult[]) {
    try {
      for (const item of replaced) {
        await undoPhotoName(productId, { language: item.language, photoName: item.name ?? "", previousName: item.previousName });
      }
      showToast({ id: toastId("undone"), message: t("products.names.fromPhotoUndone"), tone: "success" });
    } catch {
      showToast({ id: toastId("undo-failed"), message: t("products.names.fromPhotoUndoFailed"), tone: "neutral" });
    } finally {
      await refresh();
    }
  }

  async function start(photo: Blob, options: { automatic?: boolean } = {}) {
    if (options.automatic && typeof navigator !== "undefined" && navigator.onLine === false) return;
    setBusy(true);
    try {
      const result = await nameProductFromPhoto(productId, await photoForNaming(photo));
      await refresh();
      const replaced = result.languages.filter((item) => item.outcome === "replaced" && item.name);
      const proposed = result.languages.filter((item) => item.outcome === "proposed" && item.name);
      if (replaced.length > 0) {
        showToast({
          id: toastId("updated"),
          message: t("products.names.fromPhotoUpdated", { names: joinNames(replaced) }),
          tone: "success",
          action: { label: t("products.names.fromPhotoUndo"), run: () => void undo(replaced) }
        });
      }
      if (proposed.length > 0) setProposals(proposed);
      if (replaced.length === 0 && proposed.length === 0) {
        const read = result.languages.some((item) => item.outcome === "unchanged");
        showToast({ id: toastId("none"), message: t(read ? "products.names.fromPhotoSame" : "products.names.fromPhotoNothing"), tone: "neutral" });
      }
    } catch {
      // 503 (no model), 429, offline, an unreadable file: the person can type the name.
      showToast({ id: toastId("failed"), message: t("products.names.fromPhotoUnavailable"), tone: "neutral" });
    } finally {
      setBusy(false);
    }
  }

  async function replaceWithProposals(items: PhotoNameLanguageResult[]) {
    try {
      for (const item of items) await setProductName(productId, item.language, item.name ?? "");
    } catch {
      showToast({ id: toastId("save-failed"), message: t("products.names.saveError"), tone: "danger" });
    } finally {
      await refresh();
    }
  }

  const dialog = (
    <IonAlert
      isOpen={proposals.length > 0}
      header={t("products.names.fromPhotoProposeTitle")}
      message={t("products.names.fromPhotoProposeMessage", { names: joinNames(proposals) })}
      buttons={[
        { text: t("products.names.cancel"), role: "cancel" },
        { text: t("products.names.fromPhotoReplace"), handler: () => { const items = proposals; void replaceWithProposals(items); } }
      ]}
      onDidDismiss={() => setProposals([])}
    />
  );

  return { start, busy, dialog };
}

/** Polish first, then Russian, as on the card. */
function joinNames(items: PhotoNameLanguageResult[]): string {
  return [...items]
    .sort((left, right) => (left.language === right.language ? 0 : left.language === "pl" ? -1 : 1))
    .map((item) => item.name)
    .join(" · ");
}
