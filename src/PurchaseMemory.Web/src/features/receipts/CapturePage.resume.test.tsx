import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import * as api from "../../api/client";
import type { ReceiptUploadResult, ReceiptView } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { recentUploadsPollInterval } from "./BatchRecovery";
import { CapturePage } from "./CapturePage";
import { createMemoryStorage, createPendingBatchStore, type BatchKeyValue, type PendingBatchStore } from "./pendingBatchStore";

vi.mock("../../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../../api/client")>();
  return { ApiError: actual.ApiError, uploadReceipt: vi.fn(), listMyUnsavedReceipts: vi.fn(), processReceipt: vi.fn(), getReceipt: vi.fn(), reviewReceipt: vi.fn(), saveReceipt: vi.fn() };
});

const receipt = (id: string, status: ReceiptView["status"] = "queued", merchantName: string | null = null): ReceiptView => ({
  id, status, uploadedAt: "2026-10-12T09:05:00Z", updatedAt: "2026-10-12T09:05:00Z",
  originalFileName: "receipt.jpg", sourceImageUrl: "/image", imageParts: [{ position: 0, contentType: "image/jpeg", sizeBytes: 42, sourceImageUrl: "/image/0" }],
  provider: null, model: null, processingDurationMs: null,
  snapshot: merchantName ? { merchantName, purchasedAt: null, currency: "PLN", receiptTotal: 1, items: [] } : null,
  warnings: [], issues: [], error: null, savedPurchaseId: null
});
const uploaded = (id: string): ReceiptUploadResult => ({ receipt: receipt(id), exactDuplicate: false });
const photos = (count: number) => Array.from({ length: count }, (_, index) => new File([`receipt-${index + 1}`], `receipt-${index + 1}.jpg`, { type: "image/jpeg" }));
const textOf = async (file: File) => new TextDecoder().decode(await file.arrayBuffer());

/** One render is one page load; the store outlives it the way IndexedDB outlives a reload. */
function load(store: PendingBatchStore) {
  return render(<AppProviders><MemoryRouter><CapturePage recognition={{ available: true, mode: "test", models: [], message: null }} batchStore={store} /></MemoryRouter></AppProviders>);
}

function startBatch(files: File[]) {
  fireEvent.change(screen.getByLabelText("Выбрать из галереи"), { target: { files } });
  fireEvent.click(screen.getByRole("radio", { name: /Отдельные чеки/ }));
  fireEvent.click(screen.getByRole("button", { name: `Загрузить ${files.length} чека` }));
}

describe("capture: a batch interrupted by a reload", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.listMyUnsavedReceipts).mockResolvedValue([]);
    Object.defineProperty(URL, "createObjectURL", { configurable: true, value: vi.fn(() => "blob:receipt") });
    Object.defineProperty(URL, "revokeObjectURL", { configurable: true, value: vi.fn() });
  });

  it("offers the photos that never reached the server and sends exactly those bytes once", async () => {
    const store = createPendingBatchStore(async () => createMemoryStorage());
    vi.mocked(api.uploadReceipt)
      .mockResolvedValueOnce(uploaded("r1"))
      .mockImplementationOnce(() => new Promise(() => undefined));
    const firstLoad = load(store);
    startBatch(photos(3));
    await waitFor(() => expect(api.uploadReceipt).toHaveBeenCalledTimes(2));
    // The PWA dies while photo 2 is in flight.
    firstLoad.unmount();

    vi.mocked(api.uploadReceipt).mockResolvedValueOnce({ receipt: receipt("r2"), exactDuplicate: true }).mockResolvedValueOnce(uploaded("r3"));
    vi.mocked(api.listMyUnsavedReceipts).mockResolvedValue([receipt("r2", "processing"), receipt("r1", "ready", "Lidl")]);
    load(store);

    const notice = await screen.findByRole("region", { name: "Загрузка прервалась" });
    expect(notice).toHaveTextContent("2 фото из прошлой пачки ещё не дошли до сервера");
    // Server state: what already became a receipt, with live status.
    const recent = await screen.findByRole("region", { name: "Недавние загрузки" });
    expect(within(recent).getByRole("link", { name: /Lidl.*Готов/ })).toHaveAttribute("href", "/receipts/r1");
    expect(within(recent).getByText("Готов")).toBeInTheDocument();
    expect(within(recent).getByText("Распознаётся")).toBeInTheDocument();

    fireEvent.click(within(notice).getByRole("button", { name: "Продолжить загрузку (2 фото)" }));

    expect(await screen.findByText(/Загружено чеков: 2 из 2/)).toBeInTheDocument();
    const resent = vi.mocked(api.uploadReceipt).mock.calls.slice(2);
    expect(resent).toHaveLength(2);
    expect(resent.every(([, options]) => options?.process === true)).toBe(true);
    expect(await Promise.all(resent.map(([files]) => textOf(files[0])))).toEqual(["receipt-2", "receipt-3"]);
    // The photo whose answer was lost comes back as the existing receipt, not a new one.
    expect(within(screen.getAllByRole("listitem")[0]).getByText("Уже был загружен")).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Загрузка прервалась" })).not.toBeInTheDocument();
    expect(await store.load(null)).toBeNull();
  });

  it("keeps the rest for later when the resumed upload hits the rate limit", async () => {
    const store = createPendingBatchStore(async () => createMemoryStorage());
    await store.begin(photos(2), null);
    vi.mocked(api.uploadReceipt).mockRejectedValueOnce(new api.ApiError("Too many receipt uploads", 429, 170));
    const page = load(store);

    fireEvent.click(await screen.findByRole("button", { name: "Продолжить загрузку (2 фото)" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("примерно через 3 мин.");
    expect(api.uploadReceipt).toHaveBeenCalledTimes(1);
    page.unmount();
    load(store);
    expect(await screen.findByRole("button", { name: "Продолжить загрузку (2 фото)" })).toBeInTheDocument();
  });

  it("names the photos the device could not keep and forgets the batch on request", async () => {
    const memory = createMemoryStorage();
    const refusing: BatchKeyValue = { ...memory, put: async (key, value) => { if (key === "photo-1") throw new DOMException("full", "QuotaExceededError"); await memory.put(key, value); } };
    const store = createPendingBatchStore(async () => refusing);
    await store.begin(photos(2), null);
    load(store);

    const notice = await screen.findByRole("region", { name: "Загрузка прервалась" });
    expect(notice).toHaveTextContent("Ещё 1 фото устройство не сохранило — выберите его заново");
    expect(within(within(notice).getByRole("list", { name: "Фото, которые нужно выбрать заново" })).getByText("receipt-2.jpg")).toBeInTheDocument();
    expect(within(notice).getByRole("link", { name: "Как это работает: Несколько чеков за раз" })).toHaveAttribute("href", "/help#capture-batch");

    fireEvent.click(within(notice).getByRole("button", { name: "Не продолжать" }));

    expect(screen.queryByRole("region", { name: "Загрузка прервалась" })).not.toBeInTheDocument();
    await waitFor(async () => expect(await store.load(null)).toBeNull());
    expect(api.uploadReceipt).not.toHaveBeenCalled();
  });

  it("asks to continue or discard the interrupted batch before new photos can be picked", async () => {
    const store = createPendingBatchStore(async () => createMemoryStorage());
    await store.begin(photos(2), null);
    load(store);

    const notice = await screen.findByRole("region", { name: "Загрузка прервалась" });
    expect(notice).toHaveTextContent("Чтобы выбрать новые фото, сначала продолжите загрузку или нажмите «Не продолжать».");
    expect(screen.queryByLabelText("Выбрать из галереи")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Сфотографировать")).not.toBeInTheDocument();

    fireEvent.click(within(notice).getByRole("button", { name: "Не продолжать" }));
    expect(screen.getByLabelText("Выбрать из галереи")).toBeInTheDocument();
  });

  it("does not offer again a photo the server refused for good, but keeps one lost to the network", async () => {
    const store = createPendingBatchStore(async () => createMemoryStorage());
    vi.mocked(api.uploadReceipt)
      .mockResolvedValueOnce(uploaded("r1"))
      .mockRejectedValueOnce(new api.ApiError("Unsupported image type.", 400))
      .mockRejectedValueOnce(new TypeError("Load failed"));
    const firstLoad = load(store);
    startBatch(photos(3));

    expect(await screen.findByText(/Загружено чеков: 1 из 3/)).toBeInTheDocument();
    // In this session both failures stay visible with «Повторить».
    expect(within(screen.getAllByRole("listitem")[1]).getByText("Не удалось загрузить")).toBeInTheDocument();
    expect(within(screen.getAllByRole("listitem")[1]).getByText("Unsupported image type.")).toBeInTheDocument();
    expect(within(screen.getAllByRole("listitem")[2]).getByText("Не удалось загрузить")).toBeInTheDocument();
    firstLoad.unmount();

    load(store);
    const notice = await screen.findByRole("region", { name: "Загрузка прервалась" });
    expect(notice).toHaveTextContent("1 фото из прошлой пачки ещё не дошло до сервера");
    vi.mocked(api.uploadReceipt).mockResolvedValueOnce(uploaded("r3"));
    fireEvent.click(within(notice).getByRole("button", { name: "Продолжить загрузку (1 фото)" }));
    expect(await screen.findByText(/Загружено чеков: 1 из 1/)).toBeInTheDocument();
    expect(await textOf(vi.mocked(api.uploadReceipt).mock.calls[3][0][0])).toBe("receipt-3");
  });

  it("works as before when the device keeps nothing", async () => {
    const store = createPendingBatchStore(async () => null);
    vi.mocked(api.uploadReceipt).mockResolvedValueOnce(uploaded("r1")).mockResolvedValueOnce(uploaded("r2"));
    load(store);
    startBatch(photos(2));

    expect(await screen.findByText(/Загружено чеков: 2 из 2/)).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Загрузка прервалась" })).not.toBeInTheDocument();
  });

  it("polls the recent panel fast only while recognising, and slowly after a failed fetch", () => {
    expect(recentUploadsPollInterval([receipt("r1", "processing")], null)).toBe(2_000);
    expect(recentUploadsPollInterval([receipt("r1", "processing")], new Error("offline"))).toBe(30_000);
    expect(recentUploadsPollInterval([receipt("r1", "ready")], null)).toBe(false);
    expect(recentUploadsPollInterval(undefined, new Error("offline"))).toBe(false);
  });

  it("does not show the recent panel when there is nothing unsaved", async () => {
    load(createPendingBatchStore(async () => null));
    await waitFor(() => expect(api.listMyUnsavedReceipts).toHaveBeenCalled());
    expect(screen.queryByRole("region", { name: "Недавние загрузки" })).not.toBeInTheDocument();
  });
});
