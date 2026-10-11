import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError, getProductCatalog, getPurchase, updatePurchase } from "../../api/client";
import type { PurchaseDetail } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { PurchaseDetailPage } from "./PurchaseDetailPage";

vi.mock("../../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../../api/client")>();
  return {
    ApiError: actual.ApiError,
    getPurchase: vi.fn(),
    updatePurchase: vi.fn(),
    getProductCatalog: vi.fn(),
    listPurchaseImpressions: vi.fn(() => Promise.resolve([])),
    createImpression: vi.fn(),
    deleteImpression: vi.fn(),
    fetchProtectedBlob: vi.fn(() => new Promise(() => undefined))
  };
});

const purchase: PurchaseDetail = {
  id: "p1",
  sourceReceiptId: null,
  merchantName: "Żabka",
  purchasedAt: "2026-07-01T10:00:00Z",
  currency: "PLN",
  receiptTotal: 20,
  createdAt: "2026-07-01T10:00:00Z",
  updatedAt: "2026-07-01T10:00:00.123Z",
  items: [
    {
      id: "line-kawa", position: 0, name: "KAWA", quantity: 1, unit: "szt", unitPrice: 15, lineTotal: 15, kind: "product",
      discountedItemPosition: null, productId: "coffee", productName: "Kawa", pendingMatch: false, candidates: [], boundingBox: null
    },
    {
      id: "line-gouda", position: 1, name: "Ser Gouda 250g", quantity: 1, unit: "szt", unitPrice: null, lineTotal: 3, kind: "product",
      discountedItemPosition: null, productId: null, productName: null, pendingMatch: true,
      candidates: [{ productId: "gouda150", name: "Ser Gouda 150g", score: 0.8 }], boundingBox: null
    }
  ],
  issues: [
    { type: "receiptTotalMismatch", itemPosition: null, itemName: null, recognizedValue: "18.00", suggestedFix: "" },
    { type: "missingItemPrice", itemPosition: 1, itemName: "Ser Gouda 250g", recognizedValue: "", suggestedFix: "" }
  ]
};

function QueueProbe() {
  const location = useLocation();
  return <h1>Queue {location.search}</h1>;
}

function renderAt(path: string, state?: unknown) {
  const [pathname, search] = path.split("?");
  render(
    <AppProviders locale="ru">
      <MemoryRouter initialEntries={[{ pathname, search: search ? `?${search}` : "", state }]}>
        <Routes>
          <Route path="/purchases/:purchaseId" element={<PurchaseDetailPage />} />
          <Route path="/needs-attention" element={<QueueProbe />} />
        </Routes>
      </MemoryRouter>
    </AppProviders>
  );
}

describe("PurchaseDetailPage", () => {
  beforeEach(() => {
    vi.mocked(getPurchase).mockReset().mockResolvedValue(purchase);
    vi.mocked(updatePurchase).mockReset();
    vi.mocked(getProductCatalog).mockReset();
  });

  it("shows the saved purchase with its issues and switches to edit mode", async () => {
    renderAt("/purchases/p1");

    expect(await screen.findByRole("heading", { level: 1, name: "Żabka" })).toBeInTheDocument();
    expect(screen.getByText("Связано с «Kawa»")).toBeInTheDocument();
    expect(screen.getByText("Не удалось выбрать товар автоматически — выберите сами.")).toBeInTheDocument();
    expect(screen.getByText("Нет цены")).toBeInTheDocument();
    expect(screen.getByText("Покупка сохранена без фото чека.")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Исправить" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Исправление покупки" })).toBeInTheDocument();
    expect(screen.getByLabelText("Магазин")).toHaveFocus();
  });

  it("focuses the total, reconciles live and saves the corrected snapshot", async () => {
    vi.mocked(updatePurchase).mockResolvedValue({ ...purchase, updatedAt: "2026-07-02T00:00:00Z", issues: [] });
    renderAt("/purchases/p1?edit=1&focus=total");

    const total = await screen.findByLabelText("Итог чека");
    expect(total).toHaveFocus();
    const status = screen.getByText(/Строки дают/);
    expect(status).toHaveTextContent(/Строки дают 18,00\s?(zł|PLN), итог чека — 20,00\s?(zł|PLN)\. Разница -2,00/);

    const line = screen.getByRole("article", { name: "Строка 1" });
    fireEvent.change(within(line).getByLabelText("Сумма строки"), { target: { value: "17" } });
    expect(screen.getByText(/совпадает с итогом чека/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Сохранить исправления" }));

    await waitFor(() => expect(updatePurchase).toHaveBeenCalledTimes(1));
    const [id, body] = vi.mocked(updatePurchase).mock.calls[0];
    expect(id).toBe("p1");
    expect(body.expectedUpdatedAt).toBe(purchase.updatedAt);
    expect(body.receiptTotal).toBe(20);
    expect(body.items[0]).toMatchObject({ name: "KAWA", lineTotal: 17, productLink: "existing", productId: "coffee" });
    expect(body.items[1]).toMatchObject({ name: "Ser Gouda 250g", productLink: "auto", productId: null });
    expect(await screen.findByText("Исправления сохранены.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Сохранить исправления" })).not.toBeInTheDocument();
  });

  it("returns to «Нужно проверить» with its filters after saving a purchase opened from there", async () => {
    vi.mocked(updatePurchase).mockResolvedValue({ ...purchase, updatedAt: "2026-07-02T00:00:00Z", issues: [] });
    renderAt("/purchases/p1?edit=1&focus=total", { returnTo: "/needs-attention?category=milk&tag=kid" });

    fireEvent.click(await screen.findByRole("button", { name: "Сохранить исправления" }));
    expect(await screen.findByRole("heading", { name: "Queue ?category=milk&tag=kid" })).toBeInTheDocument();
  });

  it("keeps the way back to the queue when editing starts from the purchase view, and stays on cancel", async () => {
    vi.mocked(updatePurchase).mockResolvedValue({ ...purchase, updatedAt: "2026-07-02T00:00:00Z", issues: [] });
    renderAt("/purchases/p1", { returnTo: "/needs-attention" });

    fireEvent.click(await screen.findByRole("button", { name: "Исправить" }));
    fireEvent.click(await screen.findByRole("button", { name: "Отменить" }));
    fireEvent.click(await screen.findByRole("button", { name: "Исправить" }));
    fireEvent.click(await screen.findByRole("button", { name: "Сохранить исправления" }));
    expect(await screen.findByRole("heading", { name: /^Queue/ })).toBeInTheDocument();
  });

  it("ignores a return address that is not the review queue", async () => {
    vi.mocked(updatePurchase).mockResolvedValue({ ...purchase, updatedAt: "2026-07-02T00:00:00Z", issues: [] });
    renderAt("/purchases/p1?edit=1", { returnTo: "https://example.com/needs-attention" });

    fireEvent.click(await screen.findByRole("button", { name: "Сохранить исправления" }));
    expect(await screen.findByText("Исправления сохранены.")).toBeInTheDocument();
  });

  it("opens the product choice for a pending line and links the chosen candidate", async () => {
    vi.mocked(updatePurchase).mockResolvedValue(purchase);
    renderAt("/purchases/p1?edit=1&focus=product-1");

    const choose = await screen.findByRole("button", { name: "Выбрать товар для строки 2" });
    expect(choose).toHaveFocus();
    expect(choose).toHaveAttribute("aria-expanded", "true");
    fireEvent.click(screen.getByRole("button", { name: "Выбрать «Ser Gouda 150g»" }));

    const line = screen.getByRole("article", { name: "Строка 2" });
    expect(within(line).getByText("Связано с «Ser Gouda 150g»")).toBeInTheDocument();
    fireEvent.change(within(line).getByLabelText("Цена за единицу"), { target: { value: "3" } });
    fireEvent.click(screen.getByRole("button", { name: "Сохранить исправления" }));

    await waitFor(() => expect(updatePurchase).toHaveBeenCalled());
    expect(vi.mocked(updatePurchase).mock.calls[0][1].items[1]).toMatchObject({ unitPrice: 3, productLink: "existing", productId: "gouda150" });
  });

  it("searches the catalog or creates a new product for a line", async () => {
    vi.mocked(getProductCatalog).mockResolvedValue({
      items: [{ id: "cheese", name: "Ser żółty", normalizedName: "ser zolty", purchaseCount: 3, linkedItemCount: 3, aliasCount: 1, createdAt: "", updatedAt: "" }],
      page: 1, pageSize: 8, totalCount: 1, hasMore: false, pendingMatchCount: 0
    } as never);
    vi.mocked(updatePurchase).mockResolvedValue(purchase);
    renderAt("/purchases/p1?edit=1&focus=product-1");

    fireEvent.change(await screen.findByLabelText("Найти товар"), { target: { value: "ser" } });
    fireEvent.click(await screen.findByRole("button", { name: "Выбрать «Ser żółty»" }));
    expect(getProductCatalog).toHaveBeenCalledWith({ query: "ser", pageSize: 8 });

    const first = screen.getByRole("article", { name: "Строка 1" });
    fireEvent.click(within(first).getByRole("button", { name: "Выбрать товар для строки 1" }));
    fireEvent.click(within(first).getByRole("button", { name: "Создать новый товар" }));
    expect(within(first).getByText("Будет создан новый товар «KAWA»")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Сохранить исправления" }));
    await waitFor(() => expect(updatePurchase).toHaveBeenCalled());
    const items = vi.mocked(updatePurchase).mock.calls[0][1].items;
    expect(items[0]).toMatchObject({ productLink: "new", productId: null });
    expect(items[1]).toMatchObject({ productLink: "existing", productId: "cheese" });
  });

  it("adds and removes lines, blocks an unnamed line and cancels without saving", async () => {
    renderAt("/purchases/p1?edit=1");

    fireEvent.click(await screen.findByRole("button", { name: "Удалить строку 2" }));
    expect(screen.queryByRole("article", { name: "Строка 2" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Добавить строку" }));
    expect(screen.getByRole("article", { name: "Строка 2" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Сохранить исправления" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("У каждой строки должно быть название.");
    expect(updatePurchase).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: "Отменить" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Żabka" })).toBeInTheDocument();
    expect(screen.getByText("Ser Gouda 250g")).toBeInTheDocument();
    expect(updatePurchase).not.toHaveBeenCalled();
  });

  it("explains a conflict and reloads, and shows server validation errors", async () => {
    vi.mocked(updatePurchase)
      .mockRejectedValueOnce(new ApiError("Currency must be a three-letter ISO 4217 code.", 400))
      .mockRejectedValueOnce(new ApiError("changed", 409));
    renderAt("/purchases/p1?edit=1");

    fireEvent.click(await screen.findByRole("button", { name: "Сохранить исправления" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Не удалось сохранить: Currency must be a three-letter ISO 4217 code.");

    fireEvent.click(screen.getByRole("button", { name: "Сохранить исправления" }));
    expect(await screen.findByText(/Покупку уже изменили в другом окне/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Загрузить свежую версию" }));
    await waitFor(() => expect(getPurchase).toHaveBeenCalledTimes(2));
  });

  it("reports a purchase that does not exist", async () => {
    vi.mocked(getPurchase).mockRejectedValue(new ApiError("nope", 404));
    renderAt("/purchases/missing");
    expect(await screen.findByRole("alert")).toHaveTextContent("Такой покупки нет");
  });
});
