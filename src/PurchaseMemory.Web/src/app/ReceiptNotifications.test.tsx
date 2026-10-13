import { fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { ReceiptView } from "../api/types";
import { App } from "./App";
import { AppProviders } from "./AppProviders";

const receipt = (status: ReceiptView["status"], id = "6faf4fca-d37d-4f59-b281-a58076d12599"): ReceiptView => ({
  id, status, uploadedAt: "2026-08-03T12:00:00Z", updatedAt: "2026-08-03T12:01:00Z",
  originalFileName: "receipt.jpg", sourceImageUrl: "/image", imageParts: [{ position: 0, contentType: "image/jpeg", sizeBytes: 42, sourceImageUrl: "/image/0" }],
  provider: null, model: null, processingDurationMs: null, snapshot: null, warnings: [], issues: [], error: null, savedPurchaseId: null
});
const { listRecentReceipts, getReceipt } = vi.hoisted(() => ({ listRecentReceipts: vi.fn(), getReceipt: vi.fn() }));
vi.mock("../api/client", () => ({
  getReviewQueueCount: vi.fn().mockResolvedValue({ totalCount: 0, byKind: {} }),
  getReviewQueue: vi.fn().mockResolvedValue({ totalCount: 0, groups: [] }),
  getRecognitionStatus: vi.fn().mockResolvedValue({ available: true, mode: "test", models: [], message: null }),
  listRecentReceipts,
  getReceipt,
  listUnratedPurchaseItems: vi.fn().mockResolvedValue([]),
  createImpression: vi.fn(),
  deleteImpression: vi.fn(),
  getPurchaseFeed: vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false, truncated: false }),
  listCategories: vi.fn().mockResolvedValue([]),
  listBrands: vi.fn().mockResolvedValue([]),
  listTags: vi.fn().mockResolvedValue([]),
  fetchProtectedBlob: vi.fn(() => new Promise(() => undefined))
}));

describe("receipt terminal notifications", () => {
  it("polls only an active job, notifies its terminal transition once and opens the receipt from the toast", async () => {
    listRecentReceipts.mockResolvedValueOnce([receipt("processing")]).mockResolvedValue([receipt("ready")]);
    getReceipt.mockResolvedValue(receipt("ready"));
    window.history.replaceState({}, "", "/");
    render(<AppProviders><App /></AppProviders>);
    expect(await screen.findByText("Распознаётся")).toBeInTheDocument();
    expect(await screen.findByText("Чек готов к проверке", {}, { timeout: 3_000 })).toBeInTheDocument();
    await new Promise((resolve) => window.setTimeout(resolve, 1_350));
    expect(screen.getAllByText("Чек готов к проверке")).toHaveLength(1);
    expect(listRecentReceipts).toHaveBeenCalledTimes(2);

    fireEvent.click(screen.getByRole("button", { name: "Открыть чек" }));
    expect(window.location.pathname).toBe(`/receipts/${receipt("ready").id}`);
    window.history.replaceState({}, "", "/");
  }, 5_000);

  it("keeps polling while any receipt of a batch is active and notifies each one in turn", async () => {
    listRecentReceipts.mockReset();
    const ids = ["b1", "b2", "b3"];
    listRecentReceipts
      .mockResolvedValueOnce(ids.map((id) => receipt("queued", id)))
      .mockResolvedValueOnce([receipt("ready", "b1"), receipt("processing", "b2"), receipt("failed", "b3")])
      .mockResolvedValue([receipt("ready", "b1"), receipt("ready", "b2"), receipt("failed", "b3")]);
    render(<AppProviders><App /></AppProviders>);

    // The first poll finishes two receipts: their toasts queue up, one at a time.
    expect(await screen.findByText("Чек готов к проверке", {}, { timeout: 3_000 })).toBeInTheDocument();
    expect(screen.queryByText("Не удалось распознать чек")).not.toBeInTheDocument();
    await new Promise((resolve) => window.setTimeout(resolve, 1_400));
    // Nothing is active any more, so polling stops.
    expect(listRecentReceipts).toHaveBeenCalledTimes(3);

    const seen: string[] = [];
    for (let index = 0; index < 3; index += 1) {
      const current = screen.getByText(/Чек готов к проверке|Не удалось распознать чек/).closest("[role=status]") as HTMLElement;
      seen.push(within(current).getByText(/Чек готов к проверке|Не удалось распознать чек/).textContent ?? "");
      fireEvent.click(within(current).getByRole("button", { name: "Закрыть" }));
    }
    expect(seen).toEqual(["Чек готов к проверке", "Не удалось распознать чек", "Чек готов к проверке"]);
    expect(screen.queryByText(/Чек готов к проверке|Не удалось распознать чек/)).not.toBeInTheDocument();
  }, 8_000);
});

