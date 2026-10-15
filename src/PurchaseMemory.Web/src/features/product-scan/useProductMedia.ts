import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { compressPhoto } from "./compressImage";
import {
  deviceProductMediaStore,
  type AddBarcodeResult,
  type BarcodeSource,
  type OffProductHint,
  type ProductMedia
} from "./productMediaStore";

/*
 * React side of the product media store. Screens use only these hooks, so
 * stage 2 can move the data to the API by changing the query functions here.
 */

const store = deviceProductMediaStore;

export const productMediaQueryKey = ["product-media"] as const;
const mediaKey = (productId: string) => [...productMediaQueryKey, "product", productId] as const;
const photoKey = (photoId: string) => [...productMediaQueryKey, "photo", photoId] as const;
const barcodeKey = (code: string) => [...productMediaQueryKey, "barcode", code] as const;
const rememberedKey = [...productMediaQueryKey, "remembered"] as const;

export function useProductMedia(productId: string | null | undefined) {
  return useQuery({
    queryKey: mediaKey(productId ?? ""),
    queryFn: () => store.getProductMedia(productId ?? ""),
    enabled: Boolean(productId),
    staleTime: Infinity
  });
}

/** The product a barcode belongs to on this device, or null. */
export function useBarcodeOwner(code: string | null) {
  return useQuery({
    queryKey: barcodeKey(code ?? ""),
    queryFn: () => store.findProductByBarcode(code ?? ""),
    enabled: Boolean(code),
    staleTime: 0
  });
}

export function useRememberedBarcode(code: string | null) {
  return useQuery({
    queryKey: [...rememberedKey, code ?? ""],
    queryFn: () => store.findRememberedBarcode(code ?? ""),
    enabled: Boolean(code)
  });
}

/** An object URL for a stored photo; revoked when the photo or the component goes away. */
export function usePhotoUrl(photoId: string | null | undefined): { url: string | null; failed: boolean } {
  const blob = useQuery({
    queryKey: photoKey(photoId ?? ""),
    queryFn: () => store.getPhotoBlob(photoId ?? ""),
    enabled: Boolean(photoId),
    staleTime: Infinity,
    gcTime: 60_000
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
  return { url, failed: blob.isError || (blob.isSuccess && blob.data === null) };
}

/** Changes to one product's photos, barcodes and «Больше не покупать». Each refreshes every view of the store. */
export function useProductMediaActions(productId: string) {
  const queryClient = useQueryClient();
  const refresh = () => queryClient.invalidateQueries({ queryKey: productMediaQueryKey });

  return {
    /** Compresses the picked file (~1280 px JPEG) and keeps it. */
    async addPhoto(file: File) {
      const photo = await compressPhoto(file);
      const entry = await store.addPhoto(productId, photo);
      await refresh();
      return entry;
    },
    async removePhoto(photoId: string) {
      await store.removePhoto(productId, photoId);
      queryClient.removeQueries({ queryKey: photoKey(photoId) });
      await refresh();
    },
    async setMainPhoto(photoId: string) {
      await store.setMainPhoto(productId, photoId);
      await refresh();
    },
    async addBarcode(code: string, source: BarcodeSource): Promise<AddBarcodeResult> {
      const result = await store.addBarcode(productId, code, source);
      await refresh();
      return result;
    },
    async removeBarcode(code: string) {
      await store.removeBarcode(productId, code);
      await refresh();
    },
    async setAvoid(avoid: boolean) {
      await store.setAvoid(productId, avoid);
      await refresh();
    }
  };
}

export function useRememberBarcode() {
  const queryClient = useQueryClient();
  return async (code: string, off: OffProductHint | null) => {
    await store.rememberBarcode(code, off);
    await queryClient.invalidateQueries({ queryKey: productMediaQueryKey });
  };
}

/** Attaches a code scanned in the shop to an existing product. */
export function useLinkBarcode() {
  const queryClient = useQueryClient();
  return async (productId: string, code: string, source: BarcodeSource) => {
    const result = await store.addBarcode(productId, code, source);
    await queryClient.invalidateQueries({ queryKey: productMediaQueryKey });
    return result;
  };
}

export type { ProductMedia };
