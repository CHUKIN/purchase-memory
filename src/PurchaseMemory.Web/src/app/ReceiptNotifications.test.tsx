import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { ReceiptView } from "../api/types";
import { App } from "./App";
import { AppProviders } from "./AppProviders";

const receipt = (status: ReceiptView["status"]): ReceiptView => ({
  id: "6faf4fca-d37d-4f59-b281-a58076d12599", status, uploadedAt: "2026-08-03T12:00:00Z", updatedAt: "2026-08-03T12:01:00Z",
  originalFileName: "receipt.jpg", sourceImageUrl: "/image", imageParts: [{ position: 0, contentType: "image/jpeg", sizeBytes: 42, sourceImageUrl: "/image/0" }],
  provider: null, model: null, processingDurationMs: null, snapshot: null, warnings: [], issues: [], error: null, savedPurchaseId: null
});
const { listRecentReceipts } = vi.hoisted(() => ({ listRecentReceipts: vi.fn() }));
vi.mock("../api/client", () => ({
  getReviewQueueCount: vi.fn().mockResolvedValue({ totalCount: 0, byKind: {} }),
  getReviewQueue: vi.fn().mockResolvedValue({ totalCount: 0, groups: [] }),
  getRecognitionStatus: vi.fn().mockResolvedValue({ available: true, mode: "test", models: [], message: null }),
  listRecentReceipts,
  listUnratedPurchaseItems: vi.fn().mockResolvedValue([]),
  createImpression: vi.fn(),
  deleteImpression: vi.fn(),
  getPurchaseFeed: vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false, truncated: false }),
  listCategories: vi.fn().mockResolvedValue([]),
  listBrands: vi.fn().mockResolvedValue([]),
  listTags: vi.fn().mockResolvedValue([])
}));

describe("receipt terminal notifications", () => {
  it("polls only an active job and notifies its terminal transition once", async () => {
    listRecentReceipts.mockResolvedValueOnce([receipt("processing")]).mockResolvedValue([receipt("ready")]);
    render(<AppProviders><App /></AppProviders>);
    expect(await screen.findByText("Распознаётся")).toBeInTheDocument();
    expect(await screen.findByText("Чек готов к проверке", {}, { timeout: 3_000 })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Открыть чек" })).toHaveAttribute("href", `/receipts/${receipt("ready").id}`);
    await new Promise((resolve) => window.setTimeout(resolve, 1_350));
    expect(screen.getAllByText("Чек готов к проверке")).toHaveLength(1);
    expect(listRecentReceipts).toHaveBeenCalledTimes(2);
  }, 5_000);
});
