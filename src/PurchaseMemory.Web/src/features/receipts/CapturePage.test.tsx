import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router";
import { focusManager } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import * as api from "../../api/client";
import type { PurchaseDetail, ReceiptView } from "../../api/types";
import { approvalSyncFor } from "../line-review/approvalSync";
import { AppProviders } from "../../app/AppProviders";
import { CapturePage } from "./CapturePage";
import { ReceiptDetailPage } from "./ReceiptDetailPage";

vi.mock("../../api/client", async (importOriginal) => ({
  // The real error class: error messages check `instanceof ApiError`.
  ApiError: (await importOriginal<typeof import("../../api/client")>()).ApiError,
  uploadReceipt: vi.fn(), listMyUnsavedReceipts: vi.fn(async () => []), processReceipt: vi.fn(), getReceipt: vi.fn(), reviewReceipt: vi.fn(), saveReceipt: vi.fn(),
  getReceiptAuditTrail: vi.fn(async () => []), getPurchase: vi.fn(), postLineApprovals: vi.fn()
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

/** A saved purchase with these lines (name, sum), ids derived from the purchase id. */
function savedPurchase(id: string, lines: [string, number][]): PurchaseDetail {
  return {
    id, sourceReceiptId: null, merchantName: "Lidl", purchasedAt: null, currency: "PLN", receiptTotal: null, createdAt: "", updatedAt: "", issues: [],
    items: lines.map(([name, lineTotal], position) => ({
      id: `${id}-line-${position}`, position, name, quantity: 1, unit: null, unitPrice: lineTotal, lineTotal, kind: "product",
      discountedItemPosition: null, productId: null, productName: null, boundingBox: null, approved: false
    }))
  };
}

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
    const saveButton = await screen.findByRole("button", { name: "Сохранить покупку" });
    await waitFor(() => expect(saveButton).toBeEnabled());
    fireEvent.click(saveButton);
    await waitFor(() => expect(api.saveReceipt).toHaveBeenCalledTimes(1));
    expect(await screen.findByRole("heading", { name: "Purchase history" }, { timeout: 3000 })).toBeInTheDocument();
    expect(api.reviewReceipt).toHaveBeenCalledTimes(1);
  });

  it("shows a discount as its own line in the carousel and in the full editor, and sends it back on save", async () => {
    const ready: ReceiptView = { ...fixture("ready"), snapshot: { merchantName: "Lidl", purchasedAt: "2026-08-03T12:00:00Z", currency: "PLN", receiptTotal: 69.99, items: [
      { name: "Odkurzacz", quantity: 1, unit: null, unitPrice: 99.99, lineTotal: 99.99, kind: "product", discountedItemPosition: null },
      { name: "OPUST", quantity: null, unit: null, unitPrice: null, lineTotal: -30, kind: "discount", discountedItemPosition: 0 }
    ] } };
    vi.mocked(api.getReceipt).mockResolvedValue(ready);
    vi.mocked(api.reviewReceipt).mockResolvedValue(ready);
    vi.mocked(api.saveReceipt).mockResolvedValue({ id: "purchase-2", merchantName: "Lidl", purchasedAt: null, currency: "PLN", receiptTotal: 69.99, createdAt: ready.uploadedAt, updatedAt: ready.updatedAt, items: ready.snapshot!.items, issues: [] });
    render(<AppProviders><MemoryRouter initialEntries={[`/receipts/${ready.id}`]}><Routes><Route path="/receipts/:receiptId" element={<ReceiptDetailPage />} /><Route path="/purchases" element={<h1>Purchase history</h1>} /></Routes></MemoryRouter></AppProviders>);

    fireEvent.click(await screen.findByRole("button", { name: "Следующая строка" }));
    expect(screen.getByText("Скидка к «Odkurzacz»")).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Размер скидки"), { target: { value: "25" } });

    fireEvent.click(screen.getByRole("button", { name: /^Магазин, дата и итог/ }));
    expect(screen.getByRole("article", { name: /Скидка OPUST −25,00.*к товару Odkurzacz/ })).toBeInTheDocument();
    expect(screen.getByText("1 поз. · 1 скидка")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Сохранить покупку" }));

    await waitFor(() => expect(api.reviewReceipt).toHaveBeenCalledTimes(1));
    const sent = vi.mocked(api.reviewReceipt).mock.calls[0][1];
    expect(sent.items[1]).toMatchObject({ name: "OPUST", kind: "discount", lineTotal: -25 });
  });

  it("shows the lines right away, without the full editor, starting at the problem", async () => {
    vi.mocked(api.getReceipt).mockResolvedValue(withIssues());
    render(<AppProviders><MemoryRouter initialEntries={[`/receipts/${withIssues().id}`]}><Routes><Route path="/receipts/:receiptId" element={<ReceiptDetailPage />} /></Routes></MemoryRouter></AppProviders>);

    expect(await screen.findByLabelText("Название")).toHaveValue("Mleko");
    expect(screen.getByText("Строка 1 из 1")).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 2, name: "Lidl" })).toBeInTheDocument();
    expect(screen.getByText(/^Строки дают 4,00.* из 10,00.* · не хватает 6,00/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Сохранить покупку" })).toBeEnabled();
    // The full editor's own fields are not on screen until it is opened.
    expect(screen.queryByLabelText("Магазин")).not.toBeInTheDocument();
  });

  it("says when no lines were recognized and which receipt fields are missing, from the draft as it is now", async () => {
    const receipt: ReceiptView = { ...fixture("ready"), warnings: ["Валюта чека не распознана (сервер)."], snapshot: { merchantName: "Lidl", purchasedAt: null, currency: null, receiptTotal: 5, items: [] } };
    vi.mocked(api.getReceipt).mockResolvedValue(receipt);
    render(<AppProviders><MemoryRouter initialEntries={[`/receipts/${receipt.id}`]}><Routes><Route path="/receipts/:receiptId" element={<ReceiptDetailPage />} /></Routes></MemoryRouter></AppProviders>);

    expect(await screen.findByText("В чеке не распознано ни одной строки. Добавьте их в «Магазин, дата и итог».")).toBeInTheDocument();
    expect(screen.getByText("Нужно проверить")).toBeInTheDocument();
    expect(screen.getByText("Валюта не распознана")).toBeInTheDocument();
    expect(screen.getByText("Дата не распознана")).toBeInTheDocument();
    expect(screen.queryByText("Магазин не распознан")).not.toBeInTheDocument();
    expect(screen.queryByText("Итог чека не распознан")).not.toBeInTheDocument();
    // The server's recognition-time text is not shown on this screen: it would go stale.
    expect(screen.queryByText("Валюта чека не распознана (сервер).")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Сохранить покупку" })).toBeDisabled();

    fireEvent.click(screen.getByRole("button", { name: /^Магазин, дата и итог/ }));
    fireEvent.change(screen.getByLabelText("Валюта"), { target: { value: "PLN" } });
    fireEvent.click(screen.getByRole("button", { name: "Проверить строки по одной" }));
    expect(screen.queryByText("Валюта не распознана")).not.toBeInTheDocument();
    expect(screen.getByText("Дата не распознана")).toBeInTheDocument();
  });

  it("after «Взять сумму строк» nothing on the screen still says the sum is off", async () => {
    const receipt: ReceiptView = { ...withIssues(), warnings: ["Сумма строк (4.00) не совпадает с итогом чека (10.00)."] };
    vi.mocked(api.getReceipt).mockResolvedValue(receipt);
    render(<AppProviders><MemoryRouter initialEntries={[`/receipts/${receipt.id}`]}><Routes><Route path="/receipts/:receiptId" element={<ReceiptDetailPage />} /></Routes></MemoryRouter></AppProviders>);

    fireEvent.click(await screen.findByRole("button", { name: /^Взять сумму строк/ }));

    expect(screen.getByText("Сумма строк сходится с итогом чека")).toBeInTheDocument();
    expect(screen.queryByText(/не совпадает/)).not.toBeInTheDocument();
    expect(screen.queryByText(/не хватает/)).not.toBeInTheDocument();
    // Lidl, no date: only the date is reported.
    expect(screen.getByText("Дата не распознана")).toBeInTheDocument();
  });

  it("starts at the first line with a problem", async () => {
    const receipt: ReceiptView = { ...fixture("ready"), snapshot: { merchantName: "Lidl", purchasedAt: null, currency: "PLN", receiptTotal: 9, items: [
      { name: "Mleko", quantity: 1, unit: null, unitPrice: 4, lineTotal: 4 },
      { name: "Chleb", quantity: 1, unit: null, unitPrice: null, lineTotal: 5 }
    ] } };
    vi.mocked(api.getReceipt).mockResolvedValue(receipt);
    render(<AppProviders><MemoryRouter initialEntries={[`/receipts/${receipt.id}`]}><Routes><Route path="/receipts/:receiptId" element={<ReceiptDetailPage />} /></Routes></MemoryRouter></AppProviders>);

    expect(await screen.findByLabelText("Название")).toHaveValue("Chleb");
    expect(screen.getByText("Цена не распознана — впишите её по фото.")).toBeInTheDocument();
    expect(screen.getByText("Сумма строк сходится с итогом чека")).toBeInTheDocument();
  });

  it("the status follows the fixes: «Взять сумму строк» makes it «сходится»", async () => {
    vi.mocked(api.getReceipt).mockResolvedValue(withIssues());
    render(<AppProviders><MemoryRouter initialEntries={[`/receipts/${withIssues().id}`]}><Routes><Route path="/receipts/:receiptId" element={<ReceiptDetailPage />} /></Routes></MemoryRouter></AppProviders>);

    fireEvent.click(await screen.findByRole("button", { name: /^Взять сумму строк: 4,00/ }));
    expect(screen.getByText("Сумма строк сходится с итогом чека")).toBeInTheDocument();
    expect(screen.queryByText(/не хватает/)).not.toBeInTheDocument();
    expect(screen.getByText(/· 4,00/)).toBeInTheDocument();
  });

  it("lists what the server still finds and saves on the second, deliberate tap", async () => {
    vi.mocked(api.getReceipt).mockResolvedValue(withIssues());
    vi.mocked(api.reviewReceipt).mockResolvedValue(withIssues());
    vi.mocked(api.saveReceipt).mockResolvedValue({ id: "purchase-1", merchantName: "Lidl", purchasedAt: null, currency: "PLN", receiptTotal: 10, createdAt: "", updatedAt: "", items: [], issues: [] });
    render(<AppProviders><MemoryRouter initialEntries={[`/receipts/${withIssues().id}`]}><Routes><Route path="/receipts/:receiptId" element={<ReceiptDetailPage />} /><Route path="/purchases" element={<h1>Purchase history</h1>} /></Routes></MemoryRouter></AppProviders>);

    fireEvent.click(await screen.findByRole("button", { name: "Сохранить покупку" }));
    expect(await screen.findByText("Сумма строк не сходится с итогом чека")).toBeInTheDocument();
    expect(api.saveReceipt).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Всё равно сохранить" }));
    expect(await screen.findByRole("heading", { name: "Purchase history" })).toBeInTheDocument();
    expect(api.reviewReceipt).toHaveBeenCalledTimes(1);
  });

  it("opens the full editor from «Магазин, дата и итог» and goes back to the lines", async () => {
    vi.mocked(api.getReceipt).mockResolvedValue(withIssues());
    render(<AppProviders><MemoryRouter initialEntries={[`/receipts/${withIssues().id}`]}><Routes><Route path="/receipts/:receiptId" element={<ReceiptDetailPage />} /></Routes></MemoryRouter></AppProviders>);

    fireEvent.click(await screen.findByRole("button", { name: /^Магазин, дата и итог/ }));

    expect(screen.getByLabelText("Магазин")).toHaveValue("Lidl");
    expect(screen.getByRole("button", { name: "Добавить строку" })).toBeInTheDocument();
    expect(screen.queryByText(/^Строка \d+ из \d+$/)).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Проверить строки по одной" }));
    expect(screen.getByLabelText("Название")).toHaveValue("Mleko");
  });

  it("«Верно, дальше» marks reach the saved purchase's lines through the approval queue", async () => {
    const ready: ReceiptView = { ...fixture("ready"), snapshot: { merchantName: "Lidl", purchasedAt: null, currency: "PLN", receiptTotal: 9, items: [
      { name: "Mleko", quantity: 1, unit: null, unitPrice: 4, lineTotal: 4 },
      { name: "Chleb", quantity: 1, unit: null, unitPrice: 5, lineTotal: 5 }
    ] } };
    vi.mocked(api.getReceipt).mockResolvedValue(ready);
    vi.mocked(api.reviewReceipt).mockResolvedValue(ready);
    vi.mocked(api.saveReceipt).mockResolvedValue({ id: "purchase-9", merchantName: "Lidl", purchasedAt: null, currency: "PLN", receiptTotal: 9, createdAt: "", updatedAt: "", items: [], issues: [] });
    vi.mocked(api.getPurchase).mockResolvedValue(savedPurchase("purchase-9", [["Mleko", 4], ["Chleb", 5]]));
    vi.mocked(api.postLineApprovals).mockImplementation(async (items) => ({ items: items.map((item) => ({ ...item, status: "applied" as const, approvedAt: item.at })) }));
    render(<AppProviders><MemoryRouter initialEntries={[`/receipts/${ready.id}`]}><Routes><Route path="/receipts/:receiptId" element={<ReceiptDetailPage />} /><Route path="/purchases" element={<h1>Purchase history</h1>} /></Routes></MemoryRouter></AppProviders>);

    fireEvent.click(await screen.findByRole("button", { name: "Верно, дальше" }));
    expect(screen.getByLabelText("Название")).toHaveValue("Chleb");
    fireEvent.click(screen.getByRole("button", { name: "Сохранить покупку" }));
    expect(await screen.findByRole("heading", { name: "Purchase history" })).toBeInTheDocument();

    await waitFor(() => expect(api.getPurchase).toHaveBeenCalledWith("purchase-9"));
    Object.defineProperty(document, "visibilityState", { value: "hidden", configurable: true });
    document.dispatchEvent(new Event("visibilitychange"));
    Object.defineProperty(document, "visibilityState", { value: "visible", configurable: true });
    await waitFor(() => expect(api.postLineApprovals).toHaveBeenCalledTimes(1));
    expect(vi.mocked(api.postLineApprovals).mock.calls[0][0]).toEqual([expect.objectContaining({ purchaseId: "purchase-9", lineId: "purchase-9-line-0", approved: true })]);
  });

  it("keeps the marks visual when the saved lines do not match the draft", async () => {
    const ready: ReceiptView = { ...fixture("ready"), snapshot: { merchantName: "Lidl", purchasedAt: null, currency: "PLN", receiptTotal: 4, items: [{ name: "Mleko", quantity: 1, unit: null, unitPrice: 4, lineTotal: 4 }] } };
    vi.mocked(api.getReceipt).mockResolvedValue(ready);
    vi.mocked(api.reviewReceipt).mockResolvedValue(ready);
    vi.mocked(api.saveReceipt).mockResolvedValue({ id: "purchase-8", merchantName: "Lidl", purchasedAt: null, currency: "PLN", receiptTotal: 4, createdAt: "", updatedAt: "", items: [], issues: [] });
    vi.mocked(api.getPurchase).mockResolvedValue(savedPurchase("purchase-8", [["Mleko", 4], ["Torba", 0.5]]));
    const sync = approvalSyncFor("local");
    render(<AppProviders><MemoryRouter initialEntries={[`/receipts/${ready.id}`]}><Routes><Route path="/receipts/:receiptId" element={<ReceiptDetailPage />} /><Route path="/purchases" element={<h1>Purchase history</h1>} /></Routes></MemoryRouter></AppProviders>);

    fireEvent.click(await screen.findByRole("button", { name: "Верно" }));
    fireEvent.click(screen.getByRole("button", { name: "Сохранить покупку" }));
    expect(await screen.findByRole("heading", { name: "Purchase history" })).toBeInTheDocument();
    await waitFor(() => expect(api.getPurchase).toHaveBeenCalledWith("purchase-8"));
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(sync.pendingCount()).toBe(0);
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
    const name = await screen.findByLabelText("Название");
    fireEvent.change(name, { target: { value: "Local unsaved edit" } });
    focusManager.setFocused(false);
    focusManager.setFocused(true);
    await waitFor(() => expect(api.getReceipt).toHaveBeenCalledTimes(2));
    expect(screen.getByLabelText("Название")).toHaveValue("Local unsaved edit");
    expect(screen.getByRole("heading", { level: 2, name: "Lidl" })).toBeInTheDocument();
    focusManager.setFocused(undefined);
  });
});
