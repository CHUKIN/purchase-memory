import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";
import { AppProviders } from "./AppProviders";
import { AppRoutes } from "./App";

vi.mock("../api/client", () => ({
  getReviewQueueCount: vi.fn().mockResolvedValue({ totalCount: 0, byKind: {} }),
  getReviewQueue: vi.fn().mockResolvedValue({ totalCount: 0, groups: [] }),
  getRecognitionStatus: vi.fn().mockResolvedValue({ available: true, mode: "Hybrid", models: [], message: null }),
  listRecentReceipts: vi.fn().mockResolvedValue([]),
  getPurchaseFeed: vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false, truncated: false })
}));

vi.mock("../features/receipts/CapturePage", () => ({ CapturePage: () => <div>Capture receipt</div> }));

vi.mock("../features/receipts/ReceiptDetailPage", () => ({
  ReceiptDetailPage: () => <h1>Resumed receipt</h1>
}));

describe("receipt route contract", () => {
  it("supports a stable receipt URL for resumed processing and review", async () => {
    render(
      <AppProviders>
        <MemoryRouter initialEntries={["/receipts/63b9f379-509f-4df8-8afd-ec03246e89df"]}>
          <AppRoutes />
        </MemoryRouter>
      </AppProviders>
    );

    expect(await screen.findByRole("heading", { name: "Resumed receipt" })).toBeInTheDocument();
  });
});
