import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import {
  addProductBarcode,
  ApiError,
  createProduct,
  deleteProductBarcode,
  deleteProductPhoto,
  fetchProtectedBlob,
  forgetRememberedBarcode,
  getProductMedia,
  listRememberedBarcodes,
  lookupBarcode,
  productPhotoUrl,
  rememberBarcode,
  setMainProductPhoto,
  setProductAvoid,
  uploadProductPhoto
} from "../../api/client";
import type {
  BarcodeLookupResponse,
  BarcodeSource,
  OffProductHint,
  ProductMediaResponse,
  RememberedBarcodeResponse
} from "../../api/types";
import { compressPhoto } from "./compressImage";

/*
 * Product photos, barcodes, «Больше не покупать» and codes remembered for
 * later. Since stage 2 they live on the server and are shared by the
 * household; screens use only these hooks.
 */

export type { BarcodeSource, OffProductHint };

export interface ProductPhoto {
  id: string;
  /** Unix milliseconds. */
  createdAt: number;
  width: number;
  height: number;
  /** Bytes of the stored copy. */
  size: number;
}

export interface ProductBarcode {
  code: string;
  source: BarcodeSource;
  addedAt: number;
}

export interface AvoidMark {
  since: number;
}

export interface ProductMedia {
  productId: string;
  /** Oldest first. */
  photos: ProductPhoto[];
  /** Always one of `photos` while there are any; null without photos. */
  mainPhotoId: string | null;
  barcodes: ProductBarcode[];
  /** «Больше не покупать»; null when not marked. */
  avoid: AvoidMark | null;
}

/** A barcode scanned in the shop that is not tied to any product yet. */
export interface RememberedBarcode {
  code: string;
  rememberedAt: number;
  /** Null: Open Food Facts did not know it (or was not asked). */
  off: OffProductHint | null;
}

export type AddBarcodeResult =
  | { ok: true }
  /** The code already belongs to another product; one code, one product. */
  | { ok: false; reason: "taken"; productId: string };

export const productMediaQueryKey = ["product-media"] as const;
const mediaKey = (productId: string) => [...productMediaQueryKey, "product", productId] as const;
const photoKey = (photoId: string, variant: "photo" | "thumbnail") => [...productMediaQueryKey, "photo", photoId, variant] as const;
const barcodeKey = (code: string) => [...productMediaQueryKey, "barcode", code] as const;
const rememberedKey = [...productMediaQueryKey, "remembered"] as const;

/** Changes by the other household member show up after this, or at once after one's own change. */
const sharedStaleMs = 30_000;

export function toProductMedia(response: ProductMediaResponse): ProductMedia {
  return {
    productId: response.productId,
    photos: response.photos.map((photo) => ({
      id: photo.id,
      createdAt: Date.parse(photo.createdAt),
      width: photo.width,
      height: photo.height,
      size: photo.size
    })),
    mainPhotoId: response.mainPhotoId,
    barcodes: response.barcodes.map((barcode) => ({ code: barcode.code, source: barcode.source, addedAt: Date.parse(barcode.createdAt) })),
    avoid: response.avoid ? { since: Date.parse(response.avoid.since) } : null
  };
}

export function toRemembered(response: RememberedBarcodeResponse): RememberedBarcode {
  return { code: response.code, rememberedAt: Date.parse(response.rememberedAt), off: response.off };
}

export function useProductMedia(productId: string | null | undefined) {
  return useQuery({
    queryKey: mediaKey(productId ?? ""),
    queryFn: async () => toProductMedia(await getProductMedia(productId ?? "")),
    enabled: Boolean(productId),
    staleTime: sharedStaleMs
  });
}

/** What the server knows about a scanned code: one lookup serves the owner and the «remembered» note. */
function useBarcodeLookup<T>(code: string | null, select: (lookup: BarcodeLookupResponse) => T) {
  return useQuery({
    queryKey: barcodeKey(code ?? ""),
    queryFn: () => lookupBarcode(code ?? ""),
    enabled: Boolean(code),
    staleTime: 0,
    select
  });
}

/** The product a barcode belongs to in the household, or null. */
export function useBarcodeOwner(code: string | null) {
  return useBarcodeLookup(code, (lookup) => lookup.productIds[0] ?? null);
}

export function useRememberedBarcode(code: string | null) {
  return useBarcodeLookup(code, (lookup) => (lookup.remembered ? toRemembered(lookup.remembered) : null));
}

/** Codes «Запомнить на потом» of the household, newest first; «Ревью» counts them next to the queue. */
export function useRememberedBarcodes() {
  return useQuery({
    queryKey: [...rememberedKey, "list"],
    queryFn: async () => (await listRememberedBarcodes()).map(toRemembered),
    staleTime: sharedStaleMs
  });
}

/** Everything that shows photos, codes or the mark: media, lookups, remembered codes and list thumbnails. */
function useRefreshMedia() {
  const queryClient = useQueryClient();
  return () => Promise.all([
    queryClient.invalidateQueries({ queryKey: productMediaQueryKey }),
    queryClient.invalidateQueries({ queryKey: ["products"] })
  ]);
}

/** Drops a remembered code without linking it. */
export function useForgetBarcode() {
  const refresh = useRefreshMedia();
  return async (code: string) => {
    await forgetRememberedBarcode(code);
    await refresh();
  };
}

/**
 * An object URL for a stored photo (or its small thumbnail), fetched with the
 * session token; revoked when the photo or the component goes away.
 */
export function usePhotoUrl(
  photoId: string | null | undefined,
  variant: "photo" | "thumbnail" = "photo"
): { url: string | null; failed: boolean } {
  const blob = useQuery({
    queryKey: photoKey(photoId ?? "", variant),
    queryFn: ({ signal }) => fetchProtectedBlob(productPhotoUrl(photoId ?? "", variant), signal),
    enabled: Boolean(photoId),
    // The bytes behind a photo id never change.
    staleTime: Infinity,
    gcTime: 5 * 60_000,
    retry: (count, error) => !(error instanceof ApiError && error.status === 404) && count < 2
  });
  const [url, setUrl] = useState<string | null>(null);
  const data = blob.data ?? null;
  useEffect(() => {
    if (!data) {
      setUrl(null);
      return;
    }
    const next = URL.createObjectURL(data);
    setUrl(next);
    return () => URL.revokeObjectURL(next);
  }, [data]);
  return { url, failed: blob.isError };
}

/** The 409 of a code that is on another product, as a result instead of an error. */
async function addBarcodeResult(productId: string, code: string, source: BarcodeSource): Promise<AddBarcodeResult> {
  try {
    await addProductBarcode(productId, code, source);
    return { ok: true };
  } catch (error) {
    if (error instanceof ApiError && error.status === 409 && error.code === "barcodeTaken" && error.existingProductId) {
      return { ok: false, reason: "taken", productId: error.existingProductId };
    }
    throw error;
  }
}

export interface AddedPhoto {
  id: string;
  /** The product had no photos before (its name is then read from this one). */
  firstPhoto: boolean;
}

/** Changes to one product's photos, barcodes and «Больше не покупать». Each refreshes every view of them. */
export function useProductMediaActions(productId: string) {
  const queryClient = useQueryClient();
  const refresh = useRefreshMedia();

  return {
    /** Compresses the picked file (~1280 px JPEG) and uploads it. */
    async addPhoto(file: File): Promise<AddedPhoto> {
      const photo = await compressPhoto(file);
      const result = await uploadProductPhoto(productId, photo.blob);
      await refresh();
      return { id: result.photo.id, firstPhoto: result.firstPhoto };
    },
    /** The stored JPEG of one photo (for «Название по фото»). */
    readPhoto(photoId: string): Promise<Blob> {
      return queryClient.fetchQuery({
        queryKey: photoKey(photoId, "photo"),
        queryFn: ({ signal }) => fetchProtectedBlob(productPhotoUrl(photoId), signal),
        staleTime: Infinity
      });
    },
    async removePhoto(photoId: string) {
      await deleteProductPhoto(productId, photoId);
      queryClient.removeQueries({ queryKey: [...productMediaQueryKey, "photo", photoId] });
      await refresh();
    },
    async setMainPhoto(photoId: string) {
      await setMainProductPhoto(productId, photoId);
      await refresh();
    },
    async addBarcode(code: string, source: BarcodeSource): Promise<AddBarcodeResult> {
      const result = await addBarcodeResult(productId, code, source);
      await refresh();
      return result;
    },
    async removeBarcode(code: string) {
      await deleteProductBarcode(productId, code);
      await refresh();
    },
    async setAvoid(avoid: boolean) {
      await setProductAvoid(productId, avoid);
      await refresh();
    }
  };
}

export function useRememberBarcode() {
  const refresh = useRefreshMedia();
  return async (code: string, off: OffProductHint | null) => {
    await rememberBarcode(code, off);
    await refresh();
  };
}

/** Attaches a code scanned in the shop to an existing product. */
export function useLinkBarcode() {
  const refresh = useRefreshMedia();
  return async (productId: string, code: string, source: BarcodeSource) => {
    const result = await addBarcodeResult(productId, code, source);
    await refresh();
    return result;
  };
}

export type CreateForBarcodeResult =
  | { ok: true; productId: string }
  /** The server refused the name (400); `message` is its wording. */
  | { ok: false; reason: "invalid" | "failed"; message: string | null }
  /** The code went to another product meanwhile. */
  | { ok: false; reason: "taken" }
  /** The product exists but the code was not saved. */
  | { ok: false; reason: "notLinked" };

/**
 * «Новый товар» for a scanned or remembered code: creates the product on the
 * server (a namesake that already exists is used instead) and ties the code
 * to it. The source is `openFoodFacts` when the name was left as the database
 * gave it, `scan` otherwise. Linking also drops the remembered code.
 */
export function useCreateProductForBarcode() {
  const queryClient = useQueryClient();
  const link = useLinkBarcode();
  return async (code: string, name: string, brand: string, offName: string | null): Promise<CreateForBarcodeResult> => {
    let productId: string;
    try {
      productId = (await createProduct({ name, brand: brand.trim() || null, origin: "barcode" })).id;
    } catch (error) {
      if (error instanceof ApiError && error.status === 409 && error.existingProductId) {
        productId = error.existingProductId;
      } else {
        return error instanceof ApiError && error.status === 400
          ? { ok: false, reason: "invalid", message: error.message }
          : { ok: false, reason: "failed", message: null };
      }
    }
    await queryClient.invalidateQueries({ queryKey: ["products"] });
    try {
      const result = await link(productId, code, offName !== null && name === offName ? "openFoodFacts" : "scan");
      return result.ok ? { ok: true, productId } : { ok: false, reason: "taken" };
    } catch {
      return { ok: false, reason: "notLinked" };
    }
  };
}
