import { act, render, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { BarcodeCamera } from "./BarcodeCamera";

vi.mock("./barcodeDecoder", () => ({
  loadBarcodeDecoder: async () => ({ detect: async () => [] })
}));

class FakeTrack extends EventTarget {
  readyState: "live" | "ended" = "live";
  stop = vi.fn(() => { this.readyState = "ended"; });
  getCapabilities = () => ({});
  end() {
    this.readyState = "ended";
    this.dispatchEvent(new Event("ended"));
  }
}

let tracks: FakeTrack[] = [];
const getUserMedia = vi.fn(async () => {
  const track = new FakeTrack();
  tracks.push(track);
  return { getVideoTracks: () => [track], getTracks: () => [track] } as unknown as MediaStream;
});

function setVisibility(state: DocumentVisibilityState) {
  Object.defineProperty(document, "visibilityState", { configurable: true, value: state });
  document.dispatchEvent(new Event("visibilitychange"));
}

describe("BarcodeCamera restarts", () => {
  beforeEach(() => {
    tracks = [];
    getUserMedia.mockClear();
    Object.defineProperty(window, "isSecureContext", { configurable: true, value: true });
    Object.defineProperty(navigator, "mediaDevices", { configurable: true, value: { getUserMedia } });
    vi.spyOn(HTMLMediaElement.prototype, "play").mockResolvedValue(undefined);
  });
  afterEach(() => {
    // No event here: the camera is still mounted until the shared cleanup runs.
    Object.defineProperty(document, "visibilityState", { configurable: true, value: "visible" });
    vi.restoreAllMocks();
  });

  const props = { onCode: () => undefined, onManual: () => undefined };
  const started = (count: number) => waitFor(() => expect(getUserMedia).toHaveBeenCalledTimes(count));

  it("starts the camera again when the track ends or comes back from the background dead", async () => {
    render(<BarcodeCamera {...props} />);
    await started(1);
    await waitFor(() => expect(HTMLMediaElement.prototype.play).toHaveBeenCalled());

    act(() => tracks[0].end());
    await started(2);
    expect(tracks[0].stop).toHaveBeenCalled();

    await waitFor(() => expect(HTMLMediaElement.prototype.play).toHaveBeenCalledTimes(2));
    tracks[1].readyState = "ended";
    act(() => setVisibility("visible"));
    await started(3);
  });

  it("waits for the result to close before restarting a paused camera", async () => {
    const view = render(<BarcodeCamera {...props} paused />);
    await started(1);
    await waitFor(() => expect(HTMLMediaElement.prototype.play).toHaveBeenCalled());

    act(() => tracks[0].end());
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(getUserMedia).toHaveBeenCalledTimes(1);

    view.rerender(<BarcodeCamera {...props} paused={false} />);
    await started(2);
  });

  it("never restarts after it is hidden or unmounted", async () => {
    const view = render(<BarcodeCamera {...props} />);
    await started(1);
    await waitFor(() => expect(HTMLMediaElement.prototype.play).toHaveBeenCalled());

    view.rerender(<BarcodeCamera {...props} active={false} />);
    expect(tracks[0].stop).toHaveBeenCalled();
    act(() => { tracks[0].end(); setVisibility("visible"); });

    view.unmount();
    act(() => setVisibility("visible"));
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(getUserMedia).toHaveBeenCalledTimes(1);
  });
});
