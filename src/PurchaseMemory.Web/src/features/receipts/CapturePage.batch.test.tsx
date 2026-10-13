import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import * as api from "../../api/client";
import type { ReceiptUploadResult, ReceiptView } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { chooseAction } from "../../test/ionicHelpers";
import { CapturePage } from "./CapturePage";

vi.mock("../../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../../api/client")>();
  return { ApiError: actual.ApiError, uploadReceipt: vi.fn(), listMyUnsavedReceipts: vi.fn(async () => []), processReceipt: vi.fn(), getReceipt: vi.fn(), reviewReceipt: vi.fn(), saveReceipt: vi.fn() };
});

const receipt = (id: string, status: ReceiptView["status"] = "queued"): ReceiptView => ({
  id, status, uploadedAt: "2026-10-08T12:00:00Z", updatedAt: "2026-10-08T12:00:00Z",
  originalFileName: "receipt.jpg", sourceImageUrl: "/image", imageParts: [{ position: 0, contentType: "image/jpeg", sizeBytes: 42, sourceImageUrl: "/image/0" }],
  provider: null, model: null, processingDurationMs: null, snapshot: null, warnings: [], issues: [], error: null, savedPurchaseId: null
});
const uploaded = (id: string): ReceiptUploadResult => ({ receipt: receipt(id), exactDuplicate: false });
const photos = (count: number) => Array.from({ length: count }, (_, index) => new File([`receipt-${index}`], `receipt-${index + 1}.jpg`, { type: "image/jpeg" }));

function renderCapture(files: File[]) {
  render(<AppProviders><MemoryRouter><CapturePage recognition={{ available: true, mode: "test", models: [], message: null }} /></MemoryRouter></AppProviders>);
  fireEvent.change(screen.getByLabelText("Выбрать из галереи"), { target: { files } });
}

function statusOf(number: number) {
  return within(screen.getAllByRole("listitem")[number - 1]);
}

describe("capture: separate receipts batch", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    Object.defineProperty(URL, "createObjectURL", { configurable: true, value: vi.fn(() => "blob:receipt") });
    Object.defineProperty(URL, "revokeObjectURL", { configurable: true, value: vi.fn() });
  });

  it("offers the choice for several photos and keeps «parts of one receipt» as the default", async () => {
    const files = photos(3);
    vi.mocked(api.uploadReceipt).mockResolvedValue({ receipt: receipt("one", "uploaded"), exactDuplicate: true });
    renderCapture(files);

    expect(screen.getByRole("radiogroup", { name: "Что на этих фото?" })).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: /Части одного чека/ })).toBeChecked();
    expect(screen.getByRole("radio", { name: /Отдельные чеки/ })).not.toBeChecked();
    expect(screen.getByText("Часть 1")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Загрузить и распознать" }));
    await waitFor(() => expect(api.uploadReceipt).toHaveBeenCalledTimes(1));
    expect(api.uploadReceipt).toHaveBeenCalledWith(files, { process: true });
  });

  it("explains the separate mode and relabels the photos as receipts", () => {
    renderCapture(photos(3));
    fireEvent.click(screen.getByRole("radio", { name: /Отдельные чеки/ }));

    expect(screen.getByText(/3 фото — 3 распознавания/)).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Фото — каждое отдельный чек" })).toBeInTheDocument();
    expect(screen.getByText("Чек 1")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Переместить/ })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Загрузить 3 чека" })).toBeEnabled();
    expect(screen.getByRole("link", { name: "Как это работает: Несколько чеков за раз" })).toHaveAttribute("href", "/help#capture-batch");
  });

  it("uploads one receipt per photo strictly one after another", async () => {
    const files = photos(3);
    const pending: Array<(value: ReceiptUploadResult) => void> = [];
    vi.mocked(api.uploadReceipt).mockImplementation(() => new Promise((resolve) => { pending.push(resolve); }));
    renderCapture(files);
    fireEvent.click(screen.getByRole("radio", { name: /Отдельные чеки/ }));
    fireEvent.click(screen.getByRole("button", { name: "Загрузить 3 чека" }));

    await waitFor(() => expect(api.uploadReceipt).toHaveBeenCalledTimes(1));
    expect(api.uploadReceipt).toHaveBeenLastCalledWith([files[0]], { process: true });
    expect(statusOf(1).getByText("Загружаем…")).toBeInTheDocument();
    expect(statusOf(2).getByText("В очереди")).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("Загружаем фото 1 из 3…");

    pending[0](uploaded("r1"));
    await waitFor(() => expect(api.uploadReceipt).toHaveBeenCalledTimes(2));
    expect(api.uploadReceipt).toHaveBeenLastCalledWith([files[1]], { process: true });
    expect(statusOf(1).getByText("Загружен, распознаётся")).toBeInTheDocument();

    pending[1]({ receipt: receipt("r2", "ready"), exactDuplicate: true });
    await waitFor(() => expect(api.uploadReceipt).toHaveBeenCalledTimes(3));
    pending[2](uploaded("r3"));

    expect(await screen.findByText(/Загружено чеков: 3 из 3/)).toBeInTheDocument();
    expect(statusOf(2).getByText("Уже был загружен")).toBeInTheDocument();
    expect(statusOf(3).getByRole("link", { name: "Открыть чек" })).toHaveAttribute("href", "/receipts/r3");
    expect(screen.getByRole("link", { name: "На главную" })).toHaveAttribute("href", "/");
    expect(screen.queryByRole("button", { name: "Загрузить оставшиеся" })).not.toBeInTheDocument();
    // Recognition was queued by the upload itself: no second rate-limited call.
    expect(api.processReceipt).not.toHaveBeenCalled();
  });

  it("keeps going after one failed photo and retries only that photo", async () => {
    const files = photos(3);
    vi.mocked(api.uploadReceipt)
      .mockResolvedValueOnce(uploaded("r1"))
      .mockRejectedValueOnce(new Error("Receipt image is empty."))
      .mockResolvedValueOnce(uploaded("r3"))
      .mockResolvedValueOnce(uploaded("r2"));
    renderCapture(files);
    fireEvent.click(screen.getByRole("radio", { name: /Отдельные чеки/ }));
    fireEvent.click(screen.getByRole("button", { name: "Загрузить 3 чека" }));

    expect(await screen.findByText(/Загружено чеков: 2 из 3/)).toBeInTheDocument();
    expect(api.uploadReceipt).toHaveBeenCalledTimes(3);
    expect(statusOf(2).getByText("Не удалось загрузить")).toBeInTheDocument();
    expect(statusOf(2).getByText("Receipt image is empty.")).toBeInTheDocument();
    expect(statusOf(3).getByText("Загружен, распознаётся")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Повторить чек 2" }));
    expect(await screen.findByText(/Загружено чеков: 3 из 3/)).toBeInTheDocument();
    expect(api.uploadReceipt).toHaveBeenCalledTimes(4);
    expect(api.uploadReceipt).toHaveBeenLastCalledWith([files[1]], { process: true });
  });

  it("stops at the rate limit, shows the wait and resumes only on request", async () => {
    const files = photos(4);
    vi.mocked(api.uploadReceipt)
      .mockResolvedValueOnce(uploaded("r1"))
      .mockRejectedValueOnce(new api.ApiError("Too many receipt uploads", 429, 170))
      .mockResolvedValueOnce(uploaded("r2"))
      .mockResolvedValueOnce(uploaded("r3"))
      .mockResolvedValueOnce(uploaded("r4"));
    renderCapture(files);
    fireEvent.click(screen.getByRole("radio", { name: /Отдельные чеки/ }));
    fireEvent.click(screen.getByRole("button", { name: "Загрузить 4 чека" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("примерно через 3 мин.");
    expect(api.uploadReceipt).toHaveBeenCalledTimes(2);
    expect(screen.getByText(/Загружено чеков: 1 из 4/)).toBeInTheDocument();
    expect(statusOf(2).getByText("Лимит загрузок")).toBeInTheDocument();
    expect(statusOf(3).getByText("Не загружено")).toBeInTheDocument();
    expect(statusOf(4).getByText("Не загружено")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Загрузить оставшиеся" }));
    expect(await screen.findByText(/Загружено чеков: 4 из 4/)).toBeInTheDocument();
    expect(vi.mocked(api.uploadReceipt).mock.calls.slice(2).map(([sent]) => sent[0])).toEqual([files[1], files[2], files[3]]);
  });

  it("caps a batch at ten photos and lets nine photos go as separate receipts", () => {
    renderCapture(photos(11));
    fireEvent.click(screen.getByRole("radio", { name: /Отдельные чеки/ }));
    expect(screen.getByRole("alert")).toHaveTextContent("не больше 10 фото за раз");
    expect(screen.getByRole("button", { name: "Загрузить 11 чеков" })).toBeDisabled();

    fireEvent.click(statusOf(11).getByRole("button"));
    chooseAction("Удалить чек 11");
    expect(screen.getByRole("button", { name: "Загрузить 10 чеков" })).toBeEnabled();
  });

  it("starts over with an empty selection after the batch", async () => {
    vi.mocked(api.uploadReceipt).mockResolvedValueOnce(uploaded("r1")).mockResolvedValueOnce(uploaded("r2"));
    renderCapture(photos(2));
    fireEvent.click(screen.getByRole("radio", { name: /Отдельные чеки/ }));
    fireEvent.click(screen.getByRole("button", { name: "Загрузить 2 чека" }));
    fireEvent.click(await screen.findByRole("button", { name: "Загрузить другие фото" }));

    expect(screen.queryAllByRole("listitem")).toHaveLength(0);
    expect(screen.getByLabelText("Выбрать из галереи")).toBeInTheDocument();
  });
});
