import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { ReceiptStatus, ReceiptView } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { ReceiptProgress } from "./ReceiptProgress";

const view = (status: ReceiptStatus, partCount = 1): ReceiptView => ({
  id: "6faf4fca-d37d-4f59-b281-a58076d12599", status, uploadedAt: "2026-08-03T12:00:00Z", updatedAt: "2026-08-03T12:01:00Z",
  originalFileName: "receipt.jpg", sourceImageUrl: "/image", imageParts: Array.from({ length: partCount }, (_, position) => ({ position, contentType: "image/jpeg", sizeBytes: 42, sourceImageUrl: `/image/${position}` })),
  provider: null, model: null, processingDurationMs: null, snapshot: null, warnings: [], issues: [], error: status === "failed" ? "Provider failed" : null, savedPurchaseId: null
});

describe("ReceiptProgress status guidance", () => {
  it("offers queueing for an uploaded capture", () => {
    render(<AppProviders><ReceiptProgress receipt={view("uploaded")} onRetry={vi.fn()} /></AppProviders>);
    expect(screen.getByRole("button", { name: "Начать распознавание" })).toBeEnabled();
    expect(screen.getByText(/Можно уйти с этого экрана/)).toBeInTheDocument();
  });

  it("uses distinct failed and saved guidance instead of review copy", () => {
    const failed = render(<AppProviders><ReceiptProgress receipt={view("failed")} onRetry={vi.fn()} /></AppProviders>);
    expect(screen.getByText(/Фотографии сохранены/)).toBeInTheDocument();
    expect(screen.queryByText(/сначала проверьте черновик/)).not.toBeInTheDocument();
    failed.unmount();
    render(<AppProviders><ReceiptProgress receipt={view("saved")} /></AppProviders>);
    expect(screen.getByText("Покупка уже сохранена в истории.")).toBeInTheDocument();
    expect(screen.queryByText(/сначала проверьте черновик/)).not.toBeInTheDocument();
  });

  it("uses Russian one, few, and many forms for image parts", () => {
    const one = render(<AppProviders locale="ru"><ReceiptProgress receipt={view("queued", 1)} /></AppProviders>);
    expect(screen.getByText("1 часть")).toBeInTheDocument();
    one.unmount();
    const few = render(<AppProviders locale="ru"><ReceiptProgress receipt={view("queued", 3)} /></AppProviders>);
    expect(screen.getByText("3 части")).toBeInTheDocument();
    few.unmount();
    render(<AppProviders locale="ru"><ReceiptProgress receipt={view("queued", 5)} /></AppProviders>);
    expect(screen.getByText("5 частей")).toBeInTheDocument();
  });

  it("uses English singular and plural forms for image parts", () => {
    const one = render(<AppProviders locale="en"><ReceiptProgress receipt={view("queued", 1)} /></AppProviders>);
    expect(screen.getByText("1 part")).toBeInTheDocument();
    one.unmount();
    render(<AppProviders locale="en"><ReceiptProgress receipt={view("queued", 2)} /></AppProviders>);
    expect(screen.getByText("2 parts")).toBeInTheDocument();
  });
});
