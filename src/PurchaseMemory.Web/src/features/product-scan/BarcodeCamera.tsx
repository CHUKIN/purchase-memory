import { IonButton, IonSpinner } from "@ionic/react";
import { CameraOff, Flashlight, FlashlightOff, Keyboard, RotateCcw } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { normalizeBarcode } from "./barcode";
import { loadBarcodeDecoder, type BarcodeDecoder } from "./barcodeDecoder";
import "./productScan.css";

type CameraState = "starting" | "running" | "denied" | "insecure" | "unsupported" | "error";

/** `torch` is not in the DOM typings yet. */
type TorchCapabilities = MediaTrackCapabilities & { torch?: boolean };

/** The same code again within this time is the same pack still in view, not a new scan. */
const repeatGuardMs = 2500;

export interface BarcodeCameraProps {
  /** False stops the camera (stream and decoding); true starts it again. */
  active?: boolean;
  /** Keeps the camera running but ignores codes, e.g. while a result sheet is open. */
  paused?: boolean;
  /** A shop barcode was read; already normalised (UPC-A as EAN-13). */
  onCode: (code: string) => void;
  /** «Ввести код вручную», offered when the camera cannot be used. */
  onManual: () => void;
}

/**
 * The live rear camera with the barcode decoder over it. The decoder (JS +
 * WASM) is loaded on first use; the stream stops when the camera is hidden,
 * closed or unmounted, so the iPhone's camera light never stays on.
 */
export function BarcodeCamera({ active = true, paused = false, onCode, onManual }: BarcodeCameraProps) {
  const { t } = useTranslation();
  const videoRef = useRef<HTMLVideoElement>(null);
  const trackRef = useRef<MediaStreamTrack | null>(null);
  const pausedRef = useRef(paused);
  const onCodeRef = useRef(onCode);
  const [state, setState] = useState<CameraState>("starting");
  const [torchSupported, setTorchSupported] = useState(false);
  const [torchOn, setTorchOn] = useState(false);
  const [attempt, setAttempt] = useState(0);
  /** The stream died while a result was open; start it again once the result closes. */
  const restartOnResumeRef = useRef(false);

  useEffect(() => {
    pausedRef.current = paused;
    if (!paused && restartOnResumeRef.current) {
      restartOnResumeRef.current = false;
      setAttempt((value) => value + 1);
    }
  }, [paused]);
  useEffect(() => { onCodeRef.current = onCode; }, [onCode]);

  useEffect(() => {
    if (!active) return;
    let cancelled = false;
    let stream: MediaStream | null = null;
    let running = false;
    const video = videoRef.current;
    restartOnResumeRef.current = false;
    setState("starting");

    // iOS ends or freezes the camera when the app goes to the background or
    // another app takes the camera. Only this run's listeners can restart it,
    // and cleanup removes them, so nothing restarts after unmount or `active=false`.
    const restart = () => {
      if (cancelled) return;
      if (pausedRef.current) restartOnResumeRef.current = true;
      else setAttempt((value) => value + 1);
    };
    const onTrackEnded = () => restart();
    const onVisible = () => {
      if (cancelled || !running || document.visibilityState !== "visible" || !stream || !video) return;
      const track = stream.getVideoTracks()[0];
      if (!track || track.readyState === "ended") { restart(); return; }
      if (video.paused) video.play().catch(restart);
    };
    document.addEventListener("visibilitychange", onVisible);

    void (async () => {
      if (!window.isSecureContext) { setState("insecure"); return; }
      if (!navigator.mediaDevices?.getUserMedia) { setState("unsupported"); return; }

      // Fetch the decoder while iOS asks for camera permission.
      const decoderPromise = loadBarcodeDecoder();
      decoderPromise.catch(() => undefined);
      try {
        stream = await navigator.mediaDevices.getUserMedia({
          audio: false,
          video: { facingMode: { ideal: "environment" }, width: { ideal: 1280 }, height: { ideal: 720 } }
        });
      } catch (error) {
        if (cancelled) return;
        const denied = error instanceof DOMException && (error.name === "NotAllowedError" || error.name === "SecurityError");
        setState(denied ? "denied" : "error");
        return;
      }
      if (cancelled || !video) { stream.getTracks().forEach((track) => track.stop()); return; }

      const track = stream.getVideoTracks()[0] ?? null;
      trackRef.current = track;
      track?.addEventListener("ended", onTrackEnded);
      setTorchSupported(Boolean((track?.getCapabilities?.() as TorchCapabilities | undefined)?.torch));

      let decoder: BarcodeDecoder;
      try {
        decoder = await decoderPromise;
        video.srcObject = stream;
        await video.play();
      } catch {
        if (!cancelled) setState("error");
        return;
      }
      if (cancelled) return;
      running = true;
      setState("running");

      // Last sighting of each code. A code fires again only after it has been
      // out of view for a moment, so closing a result with the same pack
      // still in front of the camera does not reopen it.
      const recent = new Map<string, number>();
      let lastHit: string | null = null;
      while (!cancelled) {
        if (pausedRef.current || video.readyState < 2) {
          if (lastHit) recent.set(lastHit, performance.now());
          await sleep(120);
          continue;
        }
        let found: Array<{ rawValue: string }> = [];
        try {
          found = await decoder.detect(video);
        } catch {
          // A single bad frame is not worth stopping the camera for.
        }
        if (cancelled) break;
        const now = performance.now();
        for (const barcode of found) {
          const code = normalizeBarcode(barcode.rawValue);
          if (!/^\d{8,13}$/.test(code)) continue;
          const seen = recent.get(code);
          recent.set(code, now);
          if (seen !== undefined && now - seen < repeatGuardMs) continue;
          if (pausedRef.current) break;
          lastHit = code;
          navigator.vibrate?.(60);
          onCodeRef.current(code);
          break;
        }
        // A short breath between frames: a paint, and less battery in the shop.
        await sleep(40);
      }
    })();

    return () => {
      cancelled = true;
      document.removeEventListener("visibilitychange", onVisible);
      stream?.getVideoTracks().forEach((track) => track.removeEventListener("ended", onTrackEnded));
      stream?.getTracks().forEach((track) => track.stop());
      if (video) video.srcObject = null;
      trackRef.current = null;
      setTorchSupported(false);
      setTorchOn(false);
    };
  }, [active, attempt]);

  async function toggleTorch() {
    const track = trackRef.current;
    if (!track) return;
    const next = !torchOn;
    try {
      await track.applyConstraints({ advanced: [{ torch: next } as MediaTrackConstraintSet] });
      setTorchOn(next);
    } catch {
      setTorchSupported(false);
    }
  }

  const problem = state === "denied" || state === "insecure" || state === "unsupported" || state === "error";
  const problemText = state === "denied" ? t("productScan.cameraDenied")
    : state === "insecure" ? t("productScan.cameraInsecure")
    : state === "unsupported" ? t("productScan.cameraUnsupported")
    : t("productScan.cameraError");

  return (
    <div className="pm-scan-camera">
      <video ref={videoRef} className="pm-scan-video" playsInline muted autoPlay aria-hidden="true" />
      {state === "running" && (
        <>
          <div className="pm-scan-reticle" aria-hidden="true" />
          <p className="pm-scan-hint" role="status">{paused ? "" : t("productScan.cameraHint")}</p>
        </>
      )}
      {state === "starting" && active && (
        <div className="pm-scan-overlay" role="status">
          <IonSpinner name="crescent" aria-hidden="true" />
          <p>{t("productScan.cameraStarting")}</p>
        </div>
      )}
      {problem && (
        <div className="pm-scan-overlay" role="alert">
          <CameraOff size={44} aria-hidden="true" />
          <h2>{state === "denied" ? t("productScan.cameraDeniedTitle") : t("productScan.cameraOffTitle")}</h2>
          <p>{problemText}</p>
          <IonButton shape="round" onClick={onManual}><Keyboard size={20} aria-hidden="true" />&nbsp;{t("productScan.manualEntry")}</IonButton>
          {(state === "denied" || state === "error") && (
            <IonButton fill="clear" color="light" onClick={() => setAttempt((value) => value + 1)}>
              <RotateCcw size={18} aria-hidden="true" />&nbsp;{t("productScan.retry")}
            </IonButton>
          )}
        </div>
      )}
      {state === "running" && torchSupported && (
        <button
          type="button"
          className="pm-scan-torch"
          aria-pressed={torchOn}
          aria-label={torchOn ? t("productScan.torchOff") : t("productScan.torchOn")}
          onClick={() => void toggleTorch()}
        >
          {torchOn ? <FlashlightOff size={24} aria-hidden="true" /> : <Flashlight size={24} aria-hidden="true" />}
        </button>
      )}
    </div>
  );
}

function sleep(ms: number) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
