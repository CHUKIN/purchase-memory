/**
 * The barcode decoder: `barcode-detector` (the web BarcodeDetector API over
 * ZXing-C++ compiled to WASM, chosen in stage 1, see РЕЗУЛЬТАТЫ_ПРОТОТИП_СКАНЕРА.md).
 *
 * Loaded lazily the first time a scanner opens: the JS (~15 kB gzip) and the
 * WASM (~460 kB gzip) never sit in the main bundle, and the WASM comes from
 * our own server, not a CDN.
 */

export interface DetectedBarcode {
  rawValue: string;
  format: string;
}

export interface BarcodeDecoder {
  detect: (source: HTMLVideoElement) => Promise<DetectedBarcode[]>;
}

export const shopBarcodeFormats = ["ean_13", "ean_8", "upc_a", "upc_e"] as const;

let loading: Promise<BarcodeDecoder> | null = null;

export function loadBarcodeDecoder(): Promise<BarcodeDecoder> {
  if (loading) return loading;
  const promise = (async (): Promise<BarcodeDecoder> => {
    const [{ BarcodeDetector, prepareZXingModule }, wasm] = await Promise.all([
      import("barcode-detector/ponyfill"),
      import("zxing-wasm/reader/zxing_reader.wasm?url")
    ]);
    prepareZXingModule({
      overrides: { locateFile: (path: string, prefix: string) => (path.endsWith(".wasm") ? wasm.default : prefix + path) },
      fireImmediately: true
    });
    const detector = new BarcodeDetector({ formats: [...shopBarcodeFormats] }) as unknown as BarcodeDecoder;
    return { detect: (source: HTMLVideoElement) => detector.detect(source) };
  })();
  // A failed download (offline, a deploy in between) may succeed next time.
  promise.catch(() => { if (loading === promise) loading = null; });
  loading = promise;
  return promise;
}
