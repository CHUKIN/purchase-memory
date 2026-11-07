import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { useQueryClient, type QueryClient } from "@tanstack/react-query";
import { MemoryRouter } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError, getHaulPhotoStatus, proposeHaulPhoto, saveHaulPhoto } from "../../api/client";
import type { PurchaseDetail, PurchaseDetailItem } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { photoForHaul } from "../product-scan/compressImage";
import { HaulPhotoAction } from "./HaulPhoto";

vi.mock("../../api/client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/client")>()),
  getHaulPhotoStatus: vi.fn(),
  proposeHaulPhoto: vi.fn(),
  saveHaulPhoto: vi.fn()
}));
vi.mock("../product-scan/compressImage", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../product-scan/compressImage")>()),
  photoForHaul: vi.fn()
}));

const line = (id: string, name: string, extra: Partial<PurchaseDetailItem> = {}): PurchaseDetailItem => ({
  id, position: 0, name, quantity: 1, unit: "szt", unitPrice: 1, lineTotal: 1, kind: "product",
  discountedItemPosition: null, productId: `product-${id}`, productName: name, productNamePl: null,
  boundingBox: null, ...extra
} as PurchaseDetailItem);

const purchase = {
  id: "purchase-1", sourceReceiptId: null, merchantName: "Lidl", purchasedAt: null, currency: "PLN", receiptTotal: 10,
  createdAt: "", updatedAt: "",
  items: [
    line("l-kefir", "ACTIV KEFI NAT", { productNamePl: "Kefir naturalny" }),
    line("l-yogurt", "JOGURT NAT 150G", { quantity: 3 }),
    line("l-tissues", "CHUSTECZKI", { productId: null, productName: null }),
    line("l-discount", "RABAT", { kind: "discount", productId: null })
  ],
  issues: []
} as unknown as PurchaseDetail;

const box = { x: 0.1, y: 0.2, width: 0.3, height: 0.25 };

let queryClient: QueryClient | null = null;

function GrabClient() {
  queryClient = useQueryClient();
  return null;
}

function renderAction() {
  render(
    <AppProviders locale="ru">
      <MemoryRouter>
        <HaulPhotoAction purchase={purchase} />
        <GrabClient />
      </MemoryRouter>
    </AppProviders>
  );
}

async function takePhoto() {
  fireEvent.click(await screen.findByRole("button", { name: /Сфотографировать покупки/ }));
  const file = new File(["jpeg"], "table.jpg", { type: "image/jpeg" });
  fireEvent.change(screen.getByTestId("haul-photo-input"), { target: { files: [file] } });
}

const shrunk = new Blob(["table"], { type: "image/jpeg" });

describe("Photograph the items", () => {
  beforeEach(() => {
    vi.mocked(getHaulPhotoStatus).mockReset().mockResolvedValue({ enabled: true });
    vi.mocked(photoForHaul).mockReset().mockResolvedValue({ blob: shrunk, width: 2000, height: 1500 });
    vi.mocked(proposeHaulPhoto).mockReset().mockResolvedValue({
      lineCount: 3,
      items: [
        { index: 0, box, lineId: "l-kefir", confidence: "high" },
        { index: 1, box: { x: 0.5, y: 0.5, width: 0.2, height: 0.4 }, lineId: "l-yogurt", confidence: "medium" },
        { index: 2, box: { x: 0.7, y: 0.1, width: 0.2, height: 0.2 }, lineId: null, confidence: "low" }
      ]
    });
    vi.mocked(saveHaulPhoto).mockReset().mockResolvedValue({
      results: [
        { lineId: "l-kefir", status: "saved", productId: "product-l-kefir", photoId: "ph-1", isMain: true },
        { lineId: "l-yogurt", status: "photoLimit", productId: "product-l-yogurt", photoId: null, isMain: false }
      ]
    });
    URL.createObjectURL = vi.fn(() => "blob:table");
    URL.revokeObjectURL = vi.fn();
  });

  afterEach(() => {
    queryClient = null;
  });

  it("hides the button when the server has it switched off", async () => {
    vi.mocked(getHaulPhotoStatus).mockResolvedValue({ enabled: false });
    renderAction();

    await waitFor(() => expect(getHaulPhotoStatus).toHaveBeenCalled());
    expect(screen.queryByRole("button", { name: /Сфотографировать покупки/ })).not.toBeInTheDocument();
  });

  it("sends the shrunk photo once and shows each item with its proposed line", async () => {
    renderAction();
    await takePhoto();

    const kefir = await screen.findByRole("img", { name: "Товар 1 на фото" });
    expect(kefir).toHaveStyle({ backgroundImage: 'url("blob:table")' });
    expect(proposeHaulPhoto).toHaveBeenCalledTimes(1);
    expect(proposeHaulPhoto).toHaveBeenCalledWith("purchase-1", shrunk);
    expect(screen.getByLabelText("Строка чека для товара 1")).toHaveValue("l-kefir");
    expect(screen.getByLabelText("Строка чека для товара 2")).toHaveValue("l-yogurt");
    expect(screen.getByText("точно")).toBeInTheDocument();
    // Nothing proposed: the person picks a line or skips; «Верно» waits for a line.
    expect(screen.getByLabelText("Строка чека для товара 3")).toHaveValue("");
    expect(screen.getByText("Подходящая строка не найдена — выберите сами или пропустите.")).toBeInTheDocument();
    // A line without a product and the discount are not offered as targets.
    const options = within(screen.getByLabelText("Строка чека для товара 3")).getAllByRole("option");
    expect(options.map((option) => option.textContent)).toEqual(["Выберите строку", "Kefir naturalny", "JOGURT NAT 150G", "CHUSTECZKI — нет товара"]);
    expect(within(screen.getByLabelText("Строка чека для товара 3")).getByRole("option", { name: "CHUSTECZKI — нет товара" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Сохранить фото (0)" })).toBeDisabled();

    // What AuthGate does after a token refresh: the paid call is not repeated.
    await act(async () => { await queryClient!.invalidateQueries(); });
    expect(screen.getByRole("img", { name: "Товар 1 на фото" })).toBeInTheDocument();
    expect(proposeHaulPhoto).toHaveBeenCalledTimes(1);
  });

  it("saves only confirmed pairs with their boxes and shows what was saved and what not", async () => {
    renderAction();
    await takePhoto();

    fireEvent.click(await screen.findByRole("button", { name: "Верно: Kefir naturalny" }));
    expect(screen.getByText("Подтверждено")).toBeInTheDocument();
    // Picking another line confirms that line.
    fireEvent.change(screen.getByLabelText("Строка чека для товара 2"), { target: { value: "l-yogurt" } });
    fireEvent.click(screen.getByRole("button", { name: "Пропустить товар 3" }));
    expect(screen.getByText("Пропущено")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Сохранить фото (2)" }));

    await waitFor(() => expect(saveHaulPhoto).toHaveBeenCalledTimes(1));
    expect(saveHaulPhoto).toHaveBeenCalledWith("purchase-1", shrunk, [
      { lineId: "l-kefir", ...box },
      { lineId: "l-yogurt", x: 0.5, y: 0.5, width: 0.2, height: 0.4 }
    ]);
    expect(await screen.findByText("Сохранено фото: 1")).toBeInTheDocument();
    expect(screen.getByText("Не сохранено")).toBeInTheDocument();
    expect(screen.getByText("у товара уже 20 фото")).toBeInTheDocument();
    expect(proposeHaulPhoto).toHaveBeenCalledTimes(1);
  });

  it("«Изменить» undoes a choice and a skipped item is not sent", async () => {
    renderAction();
    await takePhoto();

    fireEvent.click(await screen.findByRole("button", { name: "Верно: Kefir naturalny" }));
    fireEvent.click(screen.getByRole("button", { name: "Изменить" }));
    expect(screen.getByRole("button", { name: "Сохранить фото (0)" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Верно: JOGURT NAT 150G" }));
    fireEvent.click(screen.getByRole("button", { name: "Сохранить фото (1)" }));

    await waitFor(() => expect(saveHaulPhoto).toHaveBeenCalledWith("purchase-1", shrunk, [
      { lineId: "l-yogurt", x: 0.5, y: 0.5, width: 0.2, height: 0.4 }
    ]));
  });

  it("says when nothing was found and offers another photo", async () => {
    vi.mocked(proposeHaulPhoto).mockResolvedValue({ lineCount: 3, items: [] });
    renderAction();
    await takePhoto();

    expect(await screen.findByText(/На фото не нашлось покупок из этого чека/)).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: /Сфотографировать ещё раз/ }).length).toBeGreaterThan(0);
  });

  it("shows an error with retry, and only the retry calls again", async () => {
    vi.mocked(proposeHaulPhoto).mockRejectedValueOnce(new ApiError("down", 503, null, null, "haulPhotoUnavailable"));
    renderAction();
    await takePhoto();

    expect(await screen.findByText("Распознавание покупок сейчас недоступно. Фото можно добавить в карточке товара.")).toBeInTheDocument();
    expect(proposeHaulPhoto).toHaveBeenCalledTimes(1);
    fireEvent.click(screen.getByRole("button", { name: /Попробовать ещё раз/ }));

    expect(await screen.findByRole("img", { name: "Товар 1 на фото" })).toBeInTheDocument();
    expect(proposeHaulPhoto).toHaveBeenCalledTimes(2);
  });

  it("switched off meanwhile: no retry, and the button status is asked again", async () => {
    vi.mocked(proposeHaulPhoto).mockRejectedValue(new ApiError("off", 503, null, null, "haulPhotoDisabled"));
    renderAction();
    await takePhoto();

    expect(await screen.findByText("Фото покупок выключено на сервере.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Попробовать ещё раз/ })).not.toBeInTheDocument();
    await waitFor(() => expect(getHaulPhotoStatus).toHaveBeenCalledTimes(2));
  });
});
