import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { dialog } from "../../test/ionicHelpers";
import { MemoryRouter, Route, Routes, useLocation } from "react-router";
import { focusManager } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import * as api from "../../api/client";
import type { ReceiptView } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { CapturePage } from "./CapturePage";
import { ReceiptDetailPage } from "./ReceiptDetailPage";

vi.mock("../../api/client", () => ({
  uploadReceipt: vi.fn(), listMyUnsavedReceipts: vi.fn(async () => []), processReceipt: vi.fn(), getReceipt: vi.fn(), reviewReceipt: vi.fn(), saveReceipt: vi.fn()
}));

const fixture = (status: ReceiptView["status"]): ReceiptView => ({
  id: "ec0383a7-10ad-4548-ad5d-bb5ecb776c9b", status, uploadedAt: "2026-08-03T12:00:00Z", updatedAt: "2026-08-03T12:01:00Z",
  originalFileName: "receipt.jpg", sourceImageUrl: "/image", imageParts: [{ position: 0, contentType: "image/jpeg", sizeBytes: 42, sourceImageUrl: "/image/0" }],
  provider: null, model: null, processingDurationMs: null, snapshot: null, warnings: [], issues: [],
  error: status === "failed" ? "Provider unavailable" : null, savedPurchaseId: null
});

const withIssues = (): ReceiptView => ({
  ...fixture("ready"),
  snapshot: { merchantName: "Lidl", purchasedAt: null, currency: "PLN", receiptTotal: 10, items: [{ name: "Mleko", quantity: 1, unit: null, unitPrice: 4, lineTotal: 4 }] },
  issues: [{ type: "receiptTotalMismatch", itemPosition: null, itemName: null, recognizedValue: "4.00 PLN", suggestedFix: "" }]
});

function LocationProbe() {
  return <output data-testid="location">{useLocation().pathname}</output>;
}

describe("receipt background flow", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    Object.defineProperty(URL, "createObjectURL", { configurable: true, value: vi.fn(() => "blob:receipt") });
    Object.defineProperty(URL, "revokeObjectURL", { configurable: true, value: vi.fn() });
  });

  it("shows an exact duplicate outcome and opens the existing stable URL without queueing a new job", async () => {
    vi.mocked(api.uploadReceipt).mockResolvedValue({ receipt: fixture("ready"), exactDuplicate: true });
    render(<AppProviders><MemoryRouter><CapturePage recognition={{ available: true, mode: "test", models: [], message: null }} /></MemoryRouter></AppProviders>);
    fireEvent.change(screen.getByLabelText("Выбрать из галереи"), { target: { files: [new File(["receipt"], "receipt.jpg", { type: "image/jpeg" })] } });
    fireEvent.click(screen.getByRole("button", { name: "Загрузить и распознать" }));
    expect(await screen.findByText("Этот чек уже загружен")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Открыть существующий чек" })).toHaveAttribute("href", `/receipts/${fixture("ready").id}`);
    expect(api.uploadReceipt).toHaveBeenCalledWith([expect.any(File)], { process: true });
    expect(api.processReceipt).not.toHaveBeenCalled();
  });

  it("resumes a failed capture from its URL and retries with persisted images", async () => {
    vi.mocked(api.getReceipt).mockResolvedValue(fixture("failed"));
    vi.mocked(api.processReceipt).mockResolvedValue(fixture("queued"));
    render(<AppProviders><MemoryRouter initialEntries={[`/receipts/${fixture("failed").id}`]}><Routes><Route path="/receipts/:receiptId" element={<ReceiptDetailPage />} /></Routes></MemoryRouter></AppProviders>);
    expect(await screen.findByText("Provider unavailable")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Повторить с сохранёнными фото" }));
    await waitFor(() => expect(api.processReceipt).toHaveBeenCalledWith(fixture("failed").id));
    expect(await screen.findByText("Чек ждёт распознавания")).toBeInTheDocument();
  });

  it("keeps the existing review and explicit save flow on the stable receipt URL", async () => {
    const ready = { ...fixture("ready"), snapshot: { merchantName: "Lidl", purchasedAt: "2026-08-03T12:00:00Z", currency: "PLN", receiptTotal: 5, items: [{ name: "Mleko", quantity: 1, unit: null, unitPrice: 5, lineTotal: 5 }] } };
    vi.mocked(api.getReceipt).mockResolvedValue(ready);
    vi.mocked(api.reviewReceipt).mockResolvedValue(ready);
    vi.mocked(api.saveReceipt).mockResolvedValue({ id: "purchase-1", merchantName: "Lidl", purchasedAt: ready.snapshot.purchasedAt, currency: "PLN", receiptTotal: 5, createdAt: ready.uploadedAt, updatedAt: ready.updatedAt, items: ready.snapshot.items, issues: [] });
    render(<AppProviders><MemoryRouter initialEntries={[`/receipts/${ready.id}`]}><Routes><Route path="/receipts/:receiptId" element={<ReceiptDetailPage />} /><Route path="/purchases" element={<h1>Purchase history</h1>} /></Routes></MemoryRouter></AppProviders>);
    fireEvent.click(await screen.findByRole("button", { name: "Сохранить покупку" }));
    expect(await screen.findByRole("heading", { name: "Purchase history" })).toBeInTheDocument();
    expect(api.reviewReceipt).toHaveBeenCalledTimes(1);
    expect(api.saveReceipt).toHaveBeenCalledTimes(1);
  });

  it("returns to «Нужно проверить» with its filters after saving a receipt opened from there", async () => {
    const ready = { ...fixture("ready"), snapshot: { merchantName: "Lidl", purchasedAt: "2026-08-03T12:00:00Z", currency: "PLN", receiptTotal: 5, items: [{ name: "Mleko", quantity: 1, unit: null, unitPrice: 5, lineTotal: 5 }] } };
    vi.mocked(api.getReceipt).mockResolvedValue(ready);
    vi.mocked(api.reviewReceipt).mockResolvedValue(ready);
    vi.mocked(api.saveReceipt).mockResolvedValue({ id: "purchase-1", merchantName: "Lidl", purchasedAt: ready.snapshot.purchasedAt, currency: "PLN", receiptTotal: 5, createdAt: ready.uploadedAt, updatedAt: ready.updatedAt, items: ready.snapshot.items, issues: [] });
    function QueueProbe() {
      const location = useLocation();
      return <h1>Queue {location.search}</h1>;
    }
    render(<AppProviders><MemoryRouter initialEntries={[{ pathname: `/receipts/${ready.id}`, state: { returnTo: "/needs-attention?category=milk&tag=kid" } }]}><Routes><Route path="/receipts/:receiptId" element={<ReceiptDetailPage />} /><Route path="/needs-attention" element={<QueueProbe />} /><Route path="/purchases" element={<h1>Purchase history</h1>} /></Routes></MemoryRouter></AppProviders>);
    fireEvent.click(await screen.findByRole("button", { name: "Сохранить покупку" }));
    expect(await screen.findByRole("heading", { name: "Queue ?category=milk&tag=kid" })).toBeInTheDocument();
  });

  it("ignores a return address that is not the review queue", async () => {
    const ready = { ...fixture("ready"), snapshot: { merchantName: "Lidl", purchasedAt: "2026-08-03T12:00:00Z", currency: "PLN", receiptTotal: 5, items: [{ name: "Mleko", quantity: 1, unit: null, unitPrice: 5, lineTotal: 5 }] } };
    vi.mocked(api.getReceipt).mockResolvedValue(ready);
    vi.mocked(api.reviewReceipt).mockResolvedValue(ready);
    vi.mocked(api.saveReceipt).mockResolvedValue({ id: "purchase-1", merchantName: "Lidl", purchasedAt: ready.snapshot.purchasedAt, currency: "PLN", receiptTotal: 5, createdAt: ready.uploadedAt, updatedAt: ready.updatedAt, items: ready.snapshot.items, issues: [] });
    render(<AppProviders><MemoryRouter initialEntries={[{ pathname: `/receipts/${ready.id}`, state: { returnTo: "https://example.com/needs-attention" } }]}><Routes><Route path="/receipts/:receiptId" element={<ReceiptDetailPage />} /><Route path="/purchases" element={<h1>Purchase history</h1>} /></Routes></MemoryRouter></AppProviders>);
    fireEvent.click(await screen.findByRole("button", { name: "Сохранить покупку" }));
    expect(await screen.findByRole("heading", { name: "Purchase history" })).toBeInTheDocument();
  });

  it("shows a discount as its own line under its product and sends it back on save", async () => {
    const ready: ReceiptView = { ...fixture("ready"), snapshot: { merchantName: "Lidl", purchasedAt: "2026-08-03T12:00:00Z", currency: "PLN", receiptTotal: 69.99, items: [
      { name: "Odkurzacz", quantity: 1, unit: null, unitPrice: 99.99, lineTotal: 99.99, kind: "product", discountedItemPosition: null },
      { name: "OPUST", quantity: null, unit: null, unitPrice: null, lineTotal: -30, kind: "discount", discountedItemPosition: 0 }
    ] } };
    vi.mocked(api.getReceipt).mockResolvedValue(ready);
    vi.mocked(api.reviewReceipt).mockResolvedValue(ready);
    vi.mocked(api.saveReceipt).mockResolvedValue({ id: "purchase-2", merchantName: "Lidl", purchasedAt: null, currency: "PLN", receiptTotal: 69.99, createdAt: ready.uploadedAt, updatedAt: ready.updatedAt, items: ready.snapshot!.items, issues: [] });
    render(<AppProviders><MemoryRouter initialEntries={[`/receipts/${ready.id}`]}><Routes><Route path="/receipts/:receiptId" element={<ReceiptDetailPage />} /><Route path="/purchases" element={<h1>Purchase history</h1>} /></Routes></MemoryRouter></AppProviders>);

    const discount = await screen.findByRole("article", { name: /Скидка OPUST −30,00.*к товару Odkurzacz/ });
    expect(screen.getByText("1 поз. · 1 скидка")).toBeInTheDocument();
    fireEvent.click(discount);
    fireEvent.change(within(dialog("Скидка, строка 2")).getByLabelText("Размер скидки"), { target: { value: "25" } });
    fireEvent.click(within(dialog("Скидка, строка 2")).getByRole("button", { name: "Готово" }));
    fireEvent.click(screen.getByRole("button", { name: "Сохранить покупку" }));

    await waitFor(() => expect(api.reviewReceipt).toHaveBeenCalledTimes(1));
    const sent = vi.mocked(api.reviewReceipt).mock.calls[0][1];
    expect(sent.items[1]).toMatchObject({ name: "OPUST", kind: "discount", lineTotal: -25 });
  });

  it("opens issue review instead of the full editor when the receipt has issues", async () => {
    vi.mocked(api.getReceipt).mockResolvedValue(withIssues());
    render(<AppProviders><MemoryRouter initialEntries={[`/receipts/${withIssues().id}`]}><Routes><Route path="/receipts/:receiptId" element={<ReceiptDetailPage />} /></Routes></MemoryRouter></AppProviders>);

    expect(await screen.findByRole("heading", { name: "Нужно уточнить" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Сумма строк не сходится с итогом чека" })).toBeInTheDocument();
    // The full editor's own fields must not be on screen until it is requested.
    expect(screen.queryByLabelText("Магазин")).not.toBeInTheDocument();
  });

  it("switches to the full editor from the explicit action and keeps it open", async () => {
    vi.mocked(api.getReceipt).mockResolvedValue(withIssues());
    render(<AppProviders><MemoryRouter initialEntries={[`/receipts/${withIssues().id}`]}><Routes><Route path="/receipts/:receiptId" element={<ReceiptDetailPage />} /></Routes></MemoryRouter></AppProviders>);

    fireEvent.click(await screen.findByRole("button", { name: "Открыть полный редактор" }));

    expect(screen.getByLabelText("Магазин")).toHaveValue("Lidl");
    expect(screen.queryByRole("heading", { name: "Нужно уточнить" })).not.toBeInTheDocument();
  });

  it("keeps the full editor as the entry point when the receipt has no issues", async () => {
    const clean = { ...fixture("ready"), snapshot: { merchantName: "Lidl", purchasedAt: null, currency: "PLN", receiptTotal: 5, items: [{ name: "Mleko", quantity: 1, unit: null, unitPrice: 5, lineTotal: 5 }] } };
    vi.mocked(api.getReceipt).mockResolvedValue(clean);
    render(<AppProviders><MemoryRouter initialEntries={[`/receipts/${clean.id}`]}><Routes><Route path="/receipts/:receiptId" element={<ReceiptDetailPage />} /></Routes></MemoryRouter></AppProviders>);

    expect(await screen.findByLabelText("Магазин")).toHaveValue("Lidl");
    expect(screen.queryByRole("heading", { name: "Нужно уточнить" })).not.toBeInTheDocument();
  });

  it("uploads and queues in one request, then opens the stable receipt URL", async () => {
    const queued = fixture("queued");
    vi.mocked(api.uploadReceipt).mockResolvedValue({ receipt: queued, exactDuplicate: false });
    vi.mocked(api.getReceipt).mockResolvedValue(queued);
    render(<AppProviders><MemoryRouter initialEntries={["/purchases/new"]}><LocationProbe /><Routes><Route path="/purchases/new" element={<CapturePage recognition={{ available: true, mode: "test", models: [], message: null }} />} /><Route path="/receipts/:receiptId" element={<ReceiptDetailPage />} /></Routes></MemoryRouter></AppProviders>);
    fireEvent.change(screen.getByLabelText("Выбрать из галереи"), { target: { files: [new File(["receipt"], "receipt.jpg", { type: "image/jpeg" })] } });
    fireEvent.click(screen.getByRole("button", { name: "Загрузить и распознать" }));

    expect(await screen.findByText("Чек ждёт распознавания")).toBeInTheDocument();
    expect(screen.getByTestId("location")).toHaveTextContent(`/receipts/${queued.id}`);
    expect(api.uploadReceipt).toHaveBeenCalledTimes(1);
    expect(api.uploadReceipt).toHaveBeenCalledWith([expect.any(File)], { process: true });
    expect(api.processReceipt).not.toHaveBeenCalled();
  });

  it("opens the stable URL when the server could not queue the upload and queues it from there", async () => {
    const uploaded = fixture("uploaded");
    vi.mocked(api.uploadReceipt).mockResolvedValue({ receipt: uploaded, exactDuplicate: false });
    vi.mocked(api.processReceipt).mockResolvedValue(fixture("queued"));
    vi.mocked(api.getReceipt).mockResolvedValue(uploaded);
    render(<AppProviders><MemoryRouter initialEntries={["/purchases/new"]}><LocationProbe /><Routes><Route path="/purchases/new" element={<CapturePage recognition={{ available: true, mode: "test", models: [], message: null }} />} /><Route path="/receipts/:receiptId" element={<ReceiptDetailPage />} /></Routes></MemoryRouter></AppProviders>);
    fireEvent.change(screen.getByLabelText("Выбрать из галереи"), { target: { files: [new File(["receipt"], "receipt.jpg", { type: "image/jpeg" })] } });
    fireEvent.click(screen.getByRole("button", { name: "Загрузить и распознать" }));

    const start = await screen.findByRole("button", { name: "Начать распознавание" });
    expect(screen.getByTestId("location")).toHaveTextContent(`/receipts/${uploaded.id}`);
    expect(api.processReceipt).not.toHaveBeenCalled();
    fireEvent.click(start);
    await waitFor(() => expect(api.processReceipt).toHaveBeenCalledTimes(1));
    expect(await screen.findByText("Чек ждёт распознавания")).toBeInTheDocument();
  });

  it.each([
    ["processing", "Чек загружен", "recognition"],
    ["ready", "Чек распознан", "review-save"],
    ["failed", "Чек не распознан", "recognition"],
    ["saved", "Чек сохранён", "recognition"]
  ] as const)("titles the %s status truthfully and links its help topic", async (status, title, topic) => {
    const receipt = fixture(status);
    vi.mocked(api.getReceipt).mockResolvedValue(receipt);
    render(<AppProviders><MemoryRouter initialEntries={[`/receipts/${receipt.id}`]}><Routes><Route path="/receipts/:receiptId" element={<ReceiptDetailPage />} /></Routes></MemoryRouter></AppProviders>);

    expect(await screen.findByRole("heading", { level: 1, name: title })).toBeInTheDocument();
    expect(document.querySelector(`[data-help-topic="${topic}"]`)).toHaveAttribute("href", `/help#${topic}`);
  });

  it("does not overwrite dirty review fields when server state refetches on focus", async () => {
    const initial = { ...fixture("ready"), snapshot: { merchantName: "Lidl", purchasedAt: null, currency: "PLN", receiptTotal: 5, items: [{ name: "Mleko", quantity: 1, unit: null, unitPrice: 5, lineTotal: 5 }] } };
    const refreshed = { ...initial, snapshot: { ...initial.snapshot, merchantName: "Server changed" } };
    vi.mocked(api.getReceipt).mockResolvedValueOnce(initial).mockResolvedValue(refreshed);
    render(<AppProviders><MemoryRouter initialEntries={[`/receipts/${initial.id}`]}><Routes><Route path="/receipts/:receiptId" element={<ReceiptDetailPage />} /></Routes></MemoryRouter></AppProviders>);
    const merchant = await screen.findByLabelText("Магазин");
    fireEvent.change(merchant, { target: { value: "Local unsaved edit" } });
    focusManager.setFocused(false);
    focusManager.setFocused(true);
    await waitFor(() => expect(api.getReceipt).toHaveBeenCalledTimes(2));
    expect(screen.getByLabelText("Магазин")).toHaveValue("Local unsaved edit");
    focusManager.setFocused(undefined);
  });
});
