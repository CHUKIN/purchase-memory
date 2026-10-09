import { render, screen } from "@testing-library/react";
import { notifications } from "@mantine/notifications";
import { describe, expect, it, vi } from "vitest";
import type { ReceiptView } from "../api/types";
import { App } from "./App";
import { AppProviders } from "./AppProviders";

const receipt = (status: ReceiptView["status"], id = "6faf4fca-d37d-4f59-b281-a58076d12599"): ReceiptView => ({
  id, status, uploadedAt: "2026-08-03T12:00:00Z", updatedAt: "2026-08-03T12:01:00Z",
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

  it("keeps polling while any receipt of a batch is active and notifies each one", async () => {
    listRecentReceipts.mockReset();
    // Mantine keeps notifications in a module-level store shared across tests.
    notifications.clean();
    const ids = ["b1", "b2", "b3"];
    listRecentReceipts
      .mockResolvedValueOnce(ids.map((id) => receipt("queued", id)))
      .mockResolvedValueOnce([receipt("ready", "b1"), receipt("processing", "b2"), receipt("failed", "b3")])
      .mockResolvedValue([receipt("ready", "b1"), receipt("ready", "b2"), receipt("failed", "b3")]);
    render(<AppProviders><App /></AppProviders>);

    expect(await screen.findByText("Не удалось распознать чек", {}, { timeout: 3_000 })).toBeInTheDocument();
    expect(screen.getAllByText("Чек готов к проверке")).toHaveLength(1);
    await new Promise((resolve) => window.setTimeout(resolve, 1_400));
    expect(screen.getAllByText("Чек готов к проверке")).toHaveLength(2);
    const hrefs = screen.getAllByRole("link", { name: "Открыть чек" }).map((link) => link.getAttribute("href"));
    expect(hrefs).toEqual(expect.arrayContaining(ids.map((id) => `/receipts/${id}`)));
    // Nothing is active any more, so polling stops.
    await new Promise((resolve) => window.setTimeout(resolve, 1_350));
    expect(listRecentReceipts).toHaveBeenCalledTimes(3);
  }, 8_000);
});
