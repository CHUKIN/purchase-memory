import { useQueryClient } from "@tanstack/react-query";
import { useEffect } from "react";
import { useTranslation } from "react-i18next";
import {
  addProductBarcode,
  rememberBarcode,
  setMainProductPhoto,
  setProductAvoid,
  uploadProductPhoto
} from "../../api/client";
import type { BatchKeyValue } from "../receipts/pendingBatchStore";
import { useToast } from "../../app/toasts";
import {
  moveDeviceMediaToServer,
  openDeviceMediaStorage,
  type DeviceMediaMoveSummary,
  type DeviceMediaTarget
} from "./deviceMediaMove";
import { productMediaQueryKey } from "./useProductMedia";

// Wrapped, not referenced: the client functions are looked up only when there is something to move.
const apiTarget: DeviceMediaTarget = {
  uploadPhoto: (productId, photo, clientId) => uploadProductPhoto(productId, photo, clientId),
  setMainPhoto: (productId, photoId) => setMainProductPhoto(productId, photoId),
  addBarcode: (productId, code, source) => addProductBarcode(productId, code, source),
  setAvoid: (productId, avoid, since) => setProductAvoid(productId, avoid, since),
  remember: (code, off) => rememberBarcode(code, off)
};

let running: Promise<DeviceMediaMoveSummary> | null = null;

/** One move per page load, however often the app shell mounts. */
export function startDeviceMediaMove(
  open: () => Promise<BatchKeyValue | null> = openDeviceMediaStorage,
  target: DeviceMediaTarget = apiTarget
): Promise<DeviceMediaMoveSummary> {
  running ??= open()
    .then((storage) => moveDeviceMediaToServer(storage, target))
    .catch(() => ({ moved: 0, skipped: 0, pending: 0 }));
  return running;
}

/** Tests only: lets the next mount start a move again. */
export function resetDeviceMediaMove() {
  running = null;
}

/**
 * Moves product photos, barcodes and marks that stage 1b kept on this phone
 * to the server after sign-in (see deviceMediaMove.ts), then says so in one
 * small toast. Renders nothing and never blocks the app.
 */
export function DeviceMediaMover() {
  const { t } = useTranslation();
  const showToast = useToast();
  const queryClient = useQueryClient();

  useEffect(() => {
    let active = true;
    void startDeviceMediaMove().then((summary) => {
      if (!active || (summary.moved === 0 && summary.skipped === 0)) return;
      void queryClient.invalidateQueries({ queryKey: productMediaQueryKey });
      void queryClient.invalidateQueries({ queryKey: ["products"] });
      const parts = [t("productScan.moveDone", { count: summary.moved })];
      if (summary.skipped > 0) parts.push(t("productScan.moveSkipped", { count: summary.skipped }));
      showToast({ id: "device-media-move", message: parts.join(" "), tone: "neutral" });
    });
    return () => { active = false; };
  }, [queryClient, showToast, t]);

  return null;
}
