import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { useQueryClient, type QueryClient } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes, useLocation } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  ApiError,
  createProduct,
  findProductsByPhoto,
  getPhotoSearchStatus,
  getProduct,
  getProductCatalog,
  listProductImpressions
} from "../../api/client";
import type { PhotoSearchMatch, ProductDetail } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { photoForNaming } from "./compressImage";
import { ScanPage } from "./ScanPage";

vi.mock("../../api/client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/client")>()),
  getPhotoSearchStatus: vi.fn(),
  findProductsByPhoto: vi.fn(),
  getProduct: vi.fn(),
  listProductImpressions: vi.fn(),
  getProductCatalog: vi.fn(),
  createProduct: vi.fn()
}));
vi.mock("./compressImage", async (importOriginal) => ({
  ...(await importOriginal<typeof import("./compressImage")>()),
  photoForNaming: vi.fn()
}));
vi.mock("./BarcodeCamera", () => ({
  BarcodeCamera: ({ paused }: { paused: boolean }) => <div data-testid="camera" data-paused={String(paused)} />
}));
vi.mock("./ProductPhoto", () => ({ ProductThumb: () => null }));
vi.mock("./useProductMedia", async (importOriginal) => ({
  ...(await importOriginal<typeof import("./useProductMedia")>()),
  useRememberedBarcodes: () => ({ data: [] }),
  useBarcodeOwner: () => ({ data: null, isPending: false, isError: false, refetch: vi.fn() }),
  useProductMedia: () => ({ data: { mainPhotoId: null, avoid: null } })
}));

const match = (productId: string, namePl: string | null, name: string, confidence: PhotoSearchMatch["confidence"], extra: Partial<PhotoSearchMatch> = {}): PhotoSearchMatch => ({
  productId, name, namePl, nameRu: null, brand: null, category: null, mainPhotoId: null,
  purchaseCount: 2, lastPurchasedAt: "2026-11-01T10:00:00Z", confidence, ...extra
});

const tomatoes = match("p-tomato", "Pomidory malinowe", "POMIDOR MALINOWY LUZ", "high", { nameRu: "Помидоры малиновые" });
const cherry = match("p-cherry", null, "POMIDOR CHERRY 250G", "medium", { brand: "Bio" });
const peppers = match("p-pepper", "Papryka czerwona", "PAPRYKA CZERW", "low", { lastPurchasedAt: null, purchaseCount: 0 });

const detail = {
  id: "p-tomato", name: "POMIDOR MALINOWY LUZ", namePl: "Pomidory malinowe", normalizedName: "", createdAt: "", updatedAt: "",
  purchaseCount: 2, linkedItemCount: 2, unpricedItemCount: 0, aliases: [], frequency: null, priceSeries: []
} as unknown as ProductDetail;

let queryClient: QueryClient | null = null;

function GrabClient() {
  queryClient = useQueryClient();
  return null;
}

function Where() {
  const location = useLocation();
  return <output data-testid="where">{location.pathname}</output>;
}

function renderScan() {
  render(
    <AppProviders locale="ru">
      <MemoryRouter initialEntries={["/scan"]}>
        <Routes>
          <Route path="/scan" element={<ScanPage />} />
          <Route path="*" element={null} />
        </Routes>
        <Where />
        <GrabClient />
      </MemoryRouter>
    </AppProviders>
  );
}

async function takePhoto() {
  fireEvent.click(await screen.findByRole("button", { name: "Найти по фото" }));
  const file = new File(["jpeg"], "tomato.jpg", { type: "image/jpeg" });
  fireEvent.change(screen.getByTestId("photo-search-input"), { target: { files: [file] } });
}

describe("Find by photo", () => {
  beforeEach(() => {
    vi.mocked(getPhotoSearchStatus).mockReset().mockResolvedValue({ enabled: true });
    vi.mocked(findProductsByPhoto).mockReset().mockResolvedValue({ matches: [tomatoes, cherry, peppers], candidateCount: 120 });
    vi.mocked(photoForNaming).mockReset().mockResolvedValue(new Blob(["small"], { type: "image/jpeg" }));
    vi.mocked(getProduct).mockReset().mockResolvedValue(detail);
    vi.mocked(listProductImpressions).mockReset().mockResolvedValue([]);
    vi.mocked(getProductCatalog).mockReset();
    vi.mocked(createProduct).mockReset();
  });

  it("hides the button when the server has it switched off", async () => {
    vi.mocked(getPhotoSearchStatus).mockResolvedValue({ enabled: false });
    renderScan();

    await waitFor(() => expect(getPhotoSearchStatus).toHaveBeenCalled());
    expect(screen.queryByRole("button", { name: "Найти по фото" })).not.toBeInTheDocument();
  });

  it("hides the button when the status cannot be read", async () => {
    vi.mocked(getPhotoSearchStatus).mockRejectedValue(new ApiError("down", 500));
    renderScan();

    await waitFor(() => expect(getPhotoSearchStatus).toHaveBeenCalled());
    expect(screen.queryByRole("button", { name: "Найти по фото" })).not.toBeInTheDocument();
  });

  it("sends the shrunk photo once, offers up to three products and opens the summary with «Это он»", async () => {
    renderScan();
    await takePhoto();

    expect(await screen.findByText("Pomidory malinowe")).toBeInTheDocument();
    expect(screen.getByText("Помидоры малиновые")).toBeInTheDocument();
    // No Polish name: the receipt text shows, with the brand.
    expect(screen.getByText("POMIDOR CHERRY 250G")).toBeInTheDocument();
    expect(screen.getByText(/Bio · похоже · покупали/)).toBeInTheDocument();
    expect(screen.getByText(/возможно · ещё не покупали/)).toBeInTheDocument();
    expect(screen.getByText(/Поиск по фото примерный/)).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: /^Это он: / })).toHaveLength(3);
    expect(findProductsByPhoto).toHaveBeenCalledTimes(1);
    expect(vi.mocked(findProductsByPhoto).mock.calls[0][0]).toBe(await vi.mocked(photoForNaming).mock.results[0].value);
    expect(screen.getByTestId("camera")).toHaveAttribute("data-paused", "true");

    fireEvent.click(screen.getByRole("button", { name: "Это он: Pomidory malinowe" }));

    expect(await screen.findByRole("button", { name: "Открыть карточку товара" })).toBeInTheDocument();
    expect(getProduct).toHaveBeenCalledWith("p-tomato");
    fireEvent.click(screen.getByRole("button", { name: "Открыть карточку товара" }));
    expect(screen.getByTestId("where")).toHaveTextContent("/products/p-tomato");
  });

  it("a session refresh that invalidates every query does not repeat the paid search", async () => {
    renderScan();
    await takePhoto();
    expect(await screen.findByText("Pomidory malinowe")).toBeInTheDocument();

    // What AuthGate does after a token refresh.
    await act(async () => { await queryClient!.invalidateQueries(); });

    expect(screen.getByText("Pomidory malinowe")).toBeInTheDocument();
    expect(screen.queryByText("Ищу по фото среди ваших товаров…")).not.toBeInTheDocument();
    expect(findProductsByPhoto).toHaveBeenCalledTimes(1);
  });

  it("a product gone since the search is not described as a barcode", async () => {
    vi.mocked(getProduct).mockRejectedValue(new ApiError("gone", 404));
    renderScan();
    await takePhoto();

    fireEvent.click(await screen.findByRole("button", { name: "Это он: Pomidory malinowe" }));

    expect(await screen.findByText("Этого товара больше нет в каталоге.")).toBeInTheDocument();
    expect(screen.queryByText(/штрихкод/)).not.toBeInTheDocument();
  });

  it("«Нет в списке» leads to the search by name, and a found product opens its summary", async () => {
    vi.mocked(getProductCatalog).mockResolvedValue({
      items: [{ id: "p-tomato", name: "POMIDOR MALINOWY LUZ", namePl: "Pomidory malinowe", brand: null, purchaseCount: 2, mainPhotoId: null }],
      page: 1, pageSize: 8, totalCount: 1, hasMore: false
    } as never);
    renderScan();
    await takePhoto();

    fireEvent.click(await screen.findByRole("button", { name: "Нет в списке" }));
    fireEvent.click(screen.getByText("Назад к вариантам"));
    fireEvent.click(await screen.findByRole("button", { name: "Нет в списке" }));
    const search = screen.getByLabelText("Найти среди своих товаров");
    fireEvent.change(search, { target: { value: "pomid" } });

    fireEvent.click(await screen.findByText("Pomidory malinowe"));
    expect(await screen.findByRole("button", { name: "Открыть карточку товара" })).toBeInTheDocument();
    expect(findProductsByPhoto).toHaveBeenCalledTimes(1);
  });

  it("says when nothing matched and offers a new product", async () => {
    vi.mocked(findProductsByPhoto).mockResolvedValue({ matches: [], candidateCount: 40 });
    vi.mocked(createProduct).mockResolvedValue({ id: "p-new" } as never);
    renderScan();
    await takePhoto();

    expect(await screen.findByText("Среди ваших товаров похожего не нашлось.")).toBeInTheDocument();
    fireEvent.click(screen.getByText("Новый товар"));
    const name = await screen.findByLabelText("Название товара");
    fireEvent.change(name, { target: { value: "Bułka kajzerka" } });
    fireEvent.click(screen.getByRole("button", { name: "Создать товар" }));

    await waitFor(() => expect(createProduct).toHaveBeenCalledWith({ name: "Bułka kajzerka", brand: null, origin: "manual" }));
    await waitFor(() => expect(screen.getByTestId("where")).toHaveTextContent("/products/p-new"));
  });

  it("shows an error with retry, and the retry searches again", async () => {
    vi.mocked(findProductsByPhoto)
      .mockRejectedValueOnce(new ApiError("unavailable", 503, null, null, "findByPhotoUnavailable"))
      .mockResolvedValueOnce({ matches: [tomatoes], candidateCount: 10 });
    renderScan();
    await takePhoto();

    expect(await screen.findByText(/Поиск по фото сейчас недоступен/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Попробовать ещё раз/ }));

    expect(await screen.findByText("Pomidory malinowe")).toBeInTheDocument();
    expect(findProductsByPhoto).toHaveBeenCalledTimes(2);
  });

  it("switched off meanwhile: says so without a retry", async () => {
    vi.mocked(findProductsByPhoto).mockRejectedValue(new ApiError("off", 503, null, null, "findByPhotoDisabled"));
    renderScan();
    await takePhoto();

    expect(await screen.findByText("Поиск по фото выключен. Найдите товар по названию.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Попробовать ещё раз/ })).not.toBeInTheDocument();
  });

  it("explains too many searches in a row", async () => {
    vi.mocked(findProductsByPhoto).mockRejectedValue(new ApiError("limited", 429, 60));
    renderScan();
    await takePhoto();

    expect(await screen.findByText(/Слишком много поисков по фото подряд/)).toBeInTheDocument();
  });
});
