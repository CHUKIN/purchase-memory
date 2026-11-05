import { ApiError } from "../../api/client";
import type { BarcodeSource, OffProductHint } from "../../api/types";
import { normalizeBarcode } from "./barcode";
import { openIndexedDbKeyValue, type BatchKeyValue } from "../receipts/pendingBatchStore";

/**
 * Stage 1b kept product photos, barcodes, «Больше не покупать» and codes
 * remembered for later on the phone only (IndexedDB
 * «purchase-memory-product-media»). Since stage 2 they live on the server;
 * this moves what a phone still holds there, once, after sign-in.
 *
 * - Idempotent: a photo goes up with its device id, so a repeat returns the
 *   stored copy; a code the product already has, a mark already set and a
 *   code already remembered change nothing.
 * - Resumable: after every product the device keeps only what is still to
 *   send. A network or server failure stops the run; the next start of the
 *   app goes on from there.
 * - Refusals that can never succeed (the product was merged away, the code
 *   belongs to another product, the photo is not a picture) are dropped and
 *   counted as skipped: keeping them would only repeat the refusal forever.
 * - Nothing is renamed from a moved photo, so no paid call happens here.
 */

/** Device storage layout of stage 1b (`productMediaStore.ts`, removed in stage 2). */
const databaseName = "purchase-memory-product-media";
const objectStoreName = "media";
const mediaPrefix = "media:";
const photoKey = (photoId: string) => `photo:${photoId}`;
const barcodeKey = (code: string) => `barcode:${code}`;
const rememberedKey = "remembered";

interface DevicePhoto {
  id: string;
}

interface DeviceBarcode {
  code: string;
  source: BarcodeSource;
}

interface DeviceMedia {
  photos: DevicePhoto[];
  mainPhotoId: string | null;
  barcodes: DeviceBarcode[];
  avoid: { since: number } | null;
}

interface DeviceRemembered {
  code: string;
  off: OffProductHint | null;
}

/** The server calls the move needs; the app passes the API client, tests a fake. */
export interface DeviceMediaTarget {
  uploadPhoto: (productId: string, photo: Blob, clientId: string) => Promise<{ photo: { id: string }; created: boolean; firstPhoto: boolean }>;
  setMainPhoto: (productId: string, photoId: string) => Promise<unknown>;
  addBarcode: (productId: string, code: string, source: BarcodeSource) => Promise<{ created: boolean }>;
  setAvoid: (productId: string, avoid: boolean, since?: string) => Promise<unknown>;
  remember: (code: string, off: OffProductHint | null) => Promise<unknown>;
}

export interface DeviceMediaMoveSummary {
  /** Items now on the server (photos, codes, marks, remembered codes). */
  moved: number;
  /** Refused for good (merged-away product, a code on another product, a broken photo); dropped. */
  skipped: number;
  /** Still on the phone because the server could not be reached; tried again next time. */
  pending: number;
}

/** The server could not be reached (network, 5xx, 401, 429): stop and keep the rest for next time. */
class StopMove extends Error {}

type Outcome<T> = { ok: true; value: T } | { ok: false; gone: boolean };

async function attempt<T>(action: () => Promise<T>): Promise<Outcome<T>> {
  try {
    return { ok: true, value: await action() };
  } catch (error) {
    // changedMeanwhile is a short race, not a refusal: keep the item for next time.
    if (error instanceof ApiError && error.code !== "changedMeanwhile" && [400, 404, 409, 413, 415].includes(error.status)) {
      // 404 on a product route: the product was merged away or never reached this server.
      return { ok: false, gone: error.status === 404 && error.code === "productNotFound" };
    }
    throw new StopMove();
  }
}

export async function moveDeviceMediaToServer(
  storage: BatchKeyValue | null,
  target: DeviceMediaTarget
): Promise<DeviceMediaMoveSummary> {
  const summary: DeviceMediaMoveSummary = { moved: 0, skipped: 0, pending: 0 };
  if (!storage?.keys) return summary;

  let keys: string[];
  try {
    keys = await storage.keys();
  } catch {
    return summary;
  }

  let stopped = false;
  for (const key of keys.filter((entry) => entry.startsWith(mediaPrefix))) {
    if (stopped) break;
    const productId = key.slice(mediaPrefix.length);
    const media = asMedia(await storage.get(key));
    const handledPhotos = new Set<string>();
    const handledCodes = new Set<string>();
    let avoidHandled = media.avoid === null;
    const total = media.photos.length + media.barcodes.length + (media.avoid ? 1 : 0);
    const dropRest = () => {
      summary.skipped += total - handledPhotos.size - handledCodes.size - (avoidHandled && media.avoid ? 1 : 0);
      media.photos.forEach((photo) => handledPhotos.add(photo.id));
      media.barcodes.forEach((barcode) => handledCodes.add(barcode.code));
      avoidHandled = true;
    };

    try {
      // Photos oldest first, as they were taken. The first one on a product
      // without photos becomes its main photo on the server; the phone's
      // choice of main photo is kept when the server had none before.
      const serverIds = new Map<string, string>();
      let firstServerId: string | null = null;
      let gone = false;
      for (const photo of media.photos) {
        const bytes = asBytes(await storage.get(photoKey(photo.id)));
        if (bytes) {
          const outcome = await attempt(() => target.uploadPhoto(productId, new Blob([bytes.bytes], { type: bytes.type }), photo.id));
          if (outcome.ok) {
            serverIds.set(photo.id, outcome.value.photo.id);
            if (outcome.value.firstPhoto) firstServerId = outcome.value.photo.id;
            if (outcome.value.created) summary.moved += 1;
          } else if (outcome.gone) {
            gone = true;
            break;
          } else {
            summary.skipped += 1;
          }
        }
        handledPhotos.add(photo.id);
      }
      if (gone) {
        dropRest();
      } else {
        const main = media.mainPhotoId ? serverIds.get(media.mainPhotoId) : undefined;
        if (firstServerId && main && main !== firstServerId) await attempt(() => target.setMainPhoto(productId, main));

        for (const barcode of media.barcodes) {
          const outcome = await attempt(() => target.addBarcode(productId, normalizeBarcode(barcode.code), barcode.source));
          if (!outcome.ok && outcome.gone) {
            gone = true;
            break;
          }
          if (outcome.ok && outcome.value.created) summary.moved += 1;
          if (!outcome.ok) summary.skipped += 1;
          handledCodes.add(barcode.code);
        }
        if (gone) {
          dropRest();
        } else if (media.avoid) {
          const since = new Date(media.avoid.since).toISOString();
          const outcome = await attempt(() => target.setAvoid(productId, true, since));
          summary[outcome.ok ? "moved" : "skipped"] += 1;
          avoidHandled = true;
        }
      }
    } catch (error) {
      if (!(error instanceof StopMove)) throw error;
      stopped = true;
    }

    const left: DeviceMedia = {
      photos: media.photos.filter((photo) => !handledPhotos.has(photo.id)),
      mainPhotoId: media.mainPhotoId,
      barcodes: media.barcodes.filter((barcode) => !handledCodes.has(barcode.code)),
      avoid: avoidHandled ? null : media.avoid
    };
    summary.pending += left.photos.length + left.barcodes.length + (left.avoid ? 1 : 0);
    await keepOnly(storage, key, media, left);
  }

  if (!stopped) {
    const remembered = asRemembered(await storage.get(rememberedKey));
    let kept: DeviceRemembered[] = [];
    for (const [index, entry] of remembered.entries()) {
      try {
        const outcome = await attempt(() => target.remember(normalizeBarcode(entry.code), entry.off));
        // 409: the code got a product meanwhile, which is what remembering was for.
        summary[outcome.ok ? "moved" : "skipped"] += 1;
      } catch {
        kept = remembered.slice(index);
        break;
      }
    }
    summary.pending += kept.length;
    if (kept.length > 0) await storage.put(rememberedKey, kept);
    else if (remembered.length > 0) await storage.delete(rememberedKey);
  }

  return summary;
}

/**
 * Keeps on the device only what is still to send for one product: its
 * remaining entries and their photo bytes. Everything sent (or dropped) is
 * deleted, with the product's barcode index keys.
 */
async function keepOnly(storage: BatchKeyValue, key: string, media: DeviceMedia, left: DeviceMedia) {
  const keptPhotos = new Set(left.photos.map((photo) => photo.id));
  for (const photo of media.photos) {
    if (!keptPhotos.has(photo.id)) await storage.delete(photoKey(photo.id));
  }
  const keptCodes = new Set(left.barcodes.map((barcode) => barcode.code));
  for (const barcode of media.barcodes) {
    if (!keptCodes.has(barcode.code)) await storage.delete(barcodeKey(barcode.code));
  }
  if (left.photos.length === 0 && left.barcodes.length === 0 && !left.avoid) {
    await storage.delete(key);
  } else {
    await storage.put(key, left);
  }
}

function asMedia(value: unknown): DeviceMedia {
  const candidate = value && typeof value === "object" ? (value as Partial<DeviceMedia>) : {};
  const photos = Array.isArray(candidate.photos)
    ? candidate.photos.filter((photo): photo is DevicePhoto => Boolean(photo) && typeof (photo as DevicePhoto).id === "string")
    : [];
  const barcodes = Array.isArray(candidate.barcodes)
    ? candidate.barcodes
      .filter((barcode): barcode is DeviceBarcode => Boolean(barcode) && typeof (barcode as DeviceBarcode).code === "string")
      .map((barcode): DeviceBarcode => ({ code: barcode.code, source: isSource(barcode.source) ? barcode.source : "scan" }))
    : [];
  return {
    photos: photos.map((photo) => ({ ...photo })),
    mainPhotoId: typeof candidate.mainPhotoId === "string" ? candidate.mainPhotoId : null,
    barcodes,
    avoid: candidate.avoid && typeof candidate.avoid.since === "number" ? { since: candidate.avoid.since } : null
  };
}

function isSource(value: unknown): value is BarcodeSource {
  return value === "scan" || value === "manual" || value === "openFoodFacts";
}

function asBytes(value: unknown): { bytes: ArrayBuffer; type: string } | null {
  const candidate = value && typeof value === "object" ? (value as { bytes?: ArrayBuffer; type?: unknown }) : null;
  // Duck-typed: a structured clone may come back from another realm's ArrayBuffer.
  return candidate?.bytes && typeof candidate.bytes.byteLength === "number"
    ? { bytes: candidate.bytes, type: typeof candidate.type === "string" ? candidate.type : "image/jpeg" }
    : null;
}

function asRemembered(value: unknown): DeviceRemembered[] {
  if (!Array.isArray(value)) return [];
  return value
    .filter((entry): entry is DeviceRemembered => Boolean(entry) && typeof (entry as DeviceRemembered).code === "string")
    .map((entry) => ({ code: entry.code, off: entry.off && typeof entry.off === "object" ? entry.off : null }));
}

/** The phone's stage-1b storage; null when the browser has no IndexedDB. */
export function openDeviceMediaStorage(): Promise<BatchKeyValue | null> {
  return openIndexedDbKeyValue(databaseName, objectStoreName);
}
