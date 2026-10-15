import { useCallback, useEffect, useRef, useState, type CSSProperties } from "react";
import { lookupOpenFoodFacts } from "./openFoodFacts";
import { formatScanResults, type ScanRecord } from "./scanResults";

/*
 * Throwaway page: measures how fast and how reliably a WASM decoder reads
 * barcodes on the owner's iPhone, and how many home products Open Food Facts
 * knows. Plain React, no Ionic and no app code, so nothing here can leak into
 * the production app. The decoder is imported lazily on «Старт».
 */

type Detector = { detect(source: HTMLVideoElement): Promise<Array<{ rawValue: string; format: string }>> };
type Phase = "idle" | "loading" | "running" | "denied" | "error";

const formats = ["ean_13", "ean_8", "upc_a", "upc_e"] as const;
const repeatGuardMs = 2500;

/** `torch` is not in the DOM typings yet. */
type TorchCapabilities = MediaTrackCapabilities & { torch?: boolean };

export function ScannerLab() {
  const videoRef = useRef<HTMLVideoElement>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const runRef = useRef(0);
  const [phase, setPhase] = useState<Phase>("idle");
  const [message, setMessage] = useState<string | null>(null);
  const [records, setRecords] = useState<ScanRecord[]>([]);
  const [last, setLast] = useState<ScanRecord | null>(null);
  const [torchSupported, setTorchSupported] = useState(false);
  const [torchOn, setTorchOn] = useState(false);
  const [frameStats, setFrameStats] = useState<{ frames: number; averageMs: number } | null>(null);
  const [copied, setCopied] = useState(false);

  const stop = useCallback(() => {
    runRef.current += 1;
    streamRef.current?.getTracks().forEach((track) => track.stop());
    streamRef.current = null;
    setTorchOn(false);
    setTorchSupported(false);
    setPhase("idle");
  }, []);

  useEffect(() => stop, [stop]);

  const start = useCallback(async () => {
    const run = ++runRef.current;
    setMessage(null);
    setPhase("loading");
    try {
      if (!window.isSecureContext) {
        setPhase("error");
        setMessage("Страница открыта не по HTTPS: iPhone не даст камеру. Откройте адрес https://… (см. инструкцию).");
        return;
      }
      if (!navigator.mediaDevices?.getUserMedia) {
        setPhase("error");
        setMessage("Этот браузер не умеет getUserMedia.");
        return;
      }

      // Lazy: the decoder (JS + ~1 MB wasm) is fetched only now, and the wasm comes from this server, not a CDN.
      const [{ BarcodeDetector, prepareZXingModule }, wasm] = await Promise.all([
        import("barcode-detector/ponyfill"),
        import("zxing-wasm/reader/zxing_reader.wasm?url")
      ]);
      prepareZXingModule({
        overrides: { locateFile: (path: string, prefix: string) => (path.endsWith(".wasm") ? wasm.default : prefix + path) },
        fireImmediately: true
      });
      const detector = new BarcodeDetector({ formats: [...formats] }) as unknown as Detector;

      let stream: MediaStream;
      try {
        stream = await navigator.mediaDevices.getUserMedia({
          audio: false,
          video: { facingMode: { ideal: "environment" }, width: { ideal: 1280 }, height: { ideal: 720 } }
        });
      } catch (error) {
        if (run !== runRef.current) return;
        const denied = error instanceof DOMException && (error.name === "NotAllowedError" || error.name === "SecurityError");
        setPhase(denied ? "denied" : "error");
        setMessage(denied
          ? "Доступ к камере запрещён. iPhone: Настройки → Safari → Камера → Спрашивать (или Разрешить), потом обновите страницу и нажмите «Старт»."
          : `Камера недоступна: ${error instanceof Error ? error.message : "неизвестная ошибка"}`);
        return;
      }
      if (run !== runRef.current) { stream.getTracks().forEach((track) => track.stop()); return; }
      streamRef.current = stream;

      const track = stream.getVideoTracks()[0];
      setTorchSupported(Boolean((track?.getCapabilities?.() as TorchCapabilities | undefined)?.torch));

      const video = videoRef.current!;
      video.srcObject = stream;
      await video.play();
      if (run !== runRef.current) return;

      setPhase("running");
      const startedAt = performance.now();
      let previousHitAt: number | null = null;
      const recentHits = new Map<string, number>();
      let frames = 0;
      let totalDecodeMs = 0;

      while (run === runRef.current) {
        if (video.readyState < 2) { await new Promise((resolve) => setTimeout(resolve, 50)); continue; }
        const frameStart = performance.now();
        let found: Array<{ rawValue: string; format: string }> = [];
        try {
          found = await detector.detect(video);
        } catch {
          // A single bad frame is not worth stopping the camera for.
        }
        const now = performance.now();
        frames += 1;
        totalDecodeMs += now - frameStart;
        if (frames % 10 === 0) setFrameStats({ frames, averageMs: totalDecodeMs / frames });

        for (const barcode of found) {
          const seen = recentHits.get(barcode.rawValue);
          if (seen !== undefined && now - seen < repeatGuardMs) continue;
          recentHits.set(barcode.rawValue, now);
          navigator.vibrate?.(60);
          const record: ScanRecord = {
            code: barcode.rawValue,
            format: barcode.format,
            msSinceStart: now - startedAt,
            msSincePrevious: previousHitAt === null ? null : now - previousHitAt,
            offFound: null,
            name: null,
            brand: null
          };
          previousHitAt = now;
          setLast(record);
          setRecords((current) => [...current, record]);
          void lookupOpenFoodFacts(barcode.rawValue, AbortSignal.timeout(10_000)).then((off) => {
            const update = (item: ScanRecord): ScanRecord => item === record
              ? { ...item, offFound: off.error ? null : off.found, name: off.name ?? (off.error ? `ошибка: ${off.error}` : null), brand: off.brand }
              : item;
            setRecords((current) => current.map(update));
            setLast((current) => (current ? update(current) : current));
          });
        }
        // Give the browser a paint between frames; the decode itself already takes tens of ms.
        await new Promise((resolve) => setTimeout(resolve, 0));
      }
    } catch (error) {
      if (run !== runRef.current) return;
      setPhase("error");
      setMessage(`Ошибка: ${error instanceof Error ? error.message : "неизвестная"}`);
    }
  }, []);

  async function toggleTorch() {
    const track = streamRef.current?.getVideoTracks()[0];
    if (!track) return;
    const next = !torchOn;
    try {
      await track.applyConstraints({ advanced: [{ torch: next } as MediaTrackConstraintSet] });
      setTorchOn(next);
    } catch {
      setMessage("Фонарик не включился на этом устройстве.");
    }
  }

  async function copyResults() {
    const text = formatScanResults(records);
    try {
      await navigator.clipboard.writeText(text);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      window.prompt("Скопируйте вручную:", text);
    }
  }

  const running = phase === "running" || phase === "loading";
  const lastHit = last;

  return (
    <main style={styles.page}>
      <h1 style={styles.h1}>Scanner lab</h1>
      <p style={styles.note}>Временная страница для замера скорости чтения штрихкодов. Не часть приложения.</p>

      <div style={styles.videoBox}>
        <video ref={videoRef} playsInline muted style={styles.video} aria-label="Камера" />
        {!running && <div style={styles.videoPlaceholder}>Камера выключена</div>}
        {phase === "running" && <div style={styles.reticle} aria-hidden="true" />}
      </div>

      <div style={styles.buttons}>
        {!running
          ? <button style={styles.primary} onClick={() => void start()}>Старт</button>
          : <button style={styles.secondary} onClick={stop}>Стоп</button>}
        {torchSupported && <button style={styles.secondary} onClick={() => void toggleTorch()}>{torchOn ? "Выключить фонарик" : "Фонарик"}</button>}
      </div>
      {phase === "loading" && <p role="status" style={styles.note}>Загружаю декодер и камеру…</p>}
      {phase === "running" && !torchSupported && <p style={styles.note}>Фонарик: браузер его не отдаёт (на iPhone Safari это обычно так).</p>}
      {frameStats && phase === "running" && <p style={styles.note}>Кадров обработано: {frameStats.frames}, в среднем {frameStats.averageMs.toFixed(0)} мс на кадр.</p>}
      {message && <p role="alert" style={styles.error}>{message}</p>}

      {lastHit && (
        <section style={styles.card} aria-live="polite">
          <div style={styles.code}>{lastHit.code}</div>
          <div style={styles.note}>
            {lastHit.format} · {Math.round(lastHit.msSinceStart)} мс от старта
            {lastHit.msSincePrevious !== null && ` · ${Math.round(lastHit.msSincePrevious)} мс от прошлого`}
          </div>
          <div>
            {lastHit.offFound === null && (lastHit.name?.startsWith("ошибка") ? lastHit.name : "Open Food Facts: запрос…")}
            {lastHit.offFound === true && <>Open Food Facts: <b>найден</b> — {[lastHit.brand, lastHit.name].filter(Boolean).join(" / ") || "без названия"}</>}
            {lastHit.offFound === false && <>Open Food Facts: <b>не найден</b></>}
          </div>
        </section>
      )}

      <section aria-label="Сканы за сессию">
        <h2 style={styles.h2}>Сканы ({records.length})</h2>
        {records.length === 0 && <p style={styles.note}>Пока пусто. Наведите камеру на штрихкод.</p>}
        <ol style={styles.list}>
          {records.map((record, index) => (
            <li key={`${record.code}-${index}`} style={styles.listItem}>
              <span style={styles.mono}>{record.code}</span>{" "}
              <span style={styles.note}>
                {Math.round(record.msSinceStart)} мс · OFF: {record.offFound === null ? "…" : record.offFound ? "да" : "нет"}
                {record.name ? ` · ${record.name}` : ""}
              </span>
            </li>
          ))}
        </ol>
        <div style={styles.buttons}>
          <button style={{ ...styles.secondary, opacity: records.length === 0 ? 0.5 : 1 }} disabled={records.length === 0} onClick={() => void copyResults()}>{copied ? "Скопировано" : "Скопировать результаты"}</button>
          <button style={{ ...styles.secondary, opacity: records.length === 0 ? 0.5 : 1 }} disabled={records.length === 0} onClick={() => { setRecords([]); setLast(null); }}>Очистить</button>
        </div>
      </section>
    </main>
  );
}

const styles: Record<string, CSSProperties> = {
  page: { maxWidth: 520, margin: "0 auto", padding: "16px 16px 40px", fontFamily: "-apple-system, BlinkMacSystemFont, sans-serif", color: "#1c1c1e" },
  h1: { fontSize: 24, margin: "0 0 4px" },
  h2: { fontSize: 18, margin: "20px 0 8px" },
  note: { margin: "4px 0", color: "#5a5a60", fontSize: 14, lineHeight: 1.4 },
  error: { margin: "8px 0", padding: 12, borderRadius: 10, background: "#fdecea", color: "#7a1f17", fontSize: 15 },
  videoBox: { position: "relative", aspectRatio: "4 / 3", background: "#111", borderRadius: 14, overflow: "hidden", margin: "12px 0" },
  video: { width: "100%", height: "100%", objectFit: "cover" },
  videoPlaceholder: { position: "absolute", inset: 0, display: "grid", placeItems: "center", color: "#bbb" },
  reticle: { position: "absolute", left: "10%", right: "10%", top: "35%", bottom: "35%", border: "2px solid rgba(255,255,255,.8)", borderRadius: 10 },
  buttons: { display: "flex", flexWrap: "wrap", gap: 8, margin: "8px 0" },
  primary: { minHeight: 48, padding: "0 28px", border: 0, borderRadius: 12, background: "#315c4b", color: "#fff", fontSize: 17 },
  secondary: { minHeight: 48, padding: "0 20px", border: "1px solid #c6c6c8", borderRadius: 12, background: "#fff", color: "#1c1c1e", fontSize: 16 },
  card: { margin: "12px 0", padding: 14, borderRadius: 12, background: "#f2f2f7", display: "grid", gap: 4 },
  code: { fontSize: 26, fontWeight: 700, fontVariantNumeric: "tabular-nums", letterSpacing: ".04em" },
  list: { margin: 0, paddingLeft: 22 },
  listItem: { padding: "4px 0", fontSize: 15 },
  mono: { fontFamily: "ui-monospace, Menlo, monospace" }
};
