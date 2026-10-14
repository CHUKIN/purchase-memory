import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError, fetchProtectedBlob, getProductCatalog, getPurchase, getReceipt, updatePurchase } from "../../api/client";
import type { PurchaseDetail, ReceiptView } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { dialog } from "../../test/ionicHelpers";
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
    fetchProtectedBlob: vi.fn(() => new Promise(() => undefined)),
    getReceipt: vi.fn()
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

/** A line row in the editor, by the start of its name. */
const lineRow = (name: string) => screen.getByRole("button", { name: new RegExp(`^${name}`) });
const save = () => screen.getByRole("button", { name: "Сохранить" });

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
    expect(screen.getByText("Сумма строк не сходится с итогом чека")).toBeInTheDocument();
    expect(screen.getByText("Покупка сохранена без фото чека.")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Исправить" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Исправить" })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByLabelText("Магазин")).toHaveFocus());
  });

  it("focuses the total, reconciles live and saves the corrected snapshot", async () => {
    vi.mocked(updatePurchase).mockResolvedValue({ ...purchase, updatedAt: "2026-07-02T00:00:00Z", issues: [] });
    renderAt("/purchases/p1?edit=1&focus=total");

    const total = await screen.findByLabelText("Итог чека");
    await waitFor(() => expect(total).toHaveFocus());
    expect(screen.getByText(/Строки дают/)).toHaveTextContent(/Строки дают 18,00\s?(zł|PLN), итог чека — 20,00\s?(zł|PLN)\. Разница -2,00/);

    fireEvent.click(lineRow("KAWA"));
    const sheet = dialog("Строка 1");
    fireEvent.change(within(sheet).getByLabelText("Сумма"), { target: { value: "17" } });
    fireEvent.click(within(sheet).getByRole("button", { name: "Готово" }));
    expect(screen.getByText(/совпадает с итогом чека/)).toBeInTheDocument();

    fireEvent.click(save());

    await waitFor(() => expect(updatePurchase).toHaveBeenCalledTimes(1));
    const [id, body] = vi.mocked(updatePurchase).mock.calls[0];
    expect(id).toBe("p1");
    expect(body.expectedUpdatedAt).toBe(purchase.updatedAt);
    expect(body.receiptTotal).toBe(20);
    expect(body.items[0]).toMatchObject({ name: "KAWA", lineTotal: 17, productLink: "existing", productId: "coffee" });
    expect(body.items[1]).toMatchObject({ name: "Ser Gouda 250g", productLink: "auto", productId: null });
    expect(await screen.findByText("Исправления сохранены.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Сохранить" })).not.toBeInTheDocument();
  });

  it("accepts a comma as the decimal separator", async () => {
    vi.mocked(updatePurchase).mockResolvedValue(purchase);
    renderAt("/purchases/p1?edit=1");

    fireEvent.change(await screen.findByLabelText("Итог чека"), { target: { value: "18,5" } });
    fireEvent.click(save());
    await waitFor(() => expect(updatePurchase).toHaveBeenCalled());
    expect(vi.mocked(updatePurchase).mock.calls[0][1].receiptTotal).toBe(18.5);
  });

  it("returns to «Нужно проверить» with its filters after saving a purchase opened from there", async () => {
    vi.mocked(updatePurchase).mockResolvedValue({ ...purchase, updatedAt: "2026-07-02T00:00:00Z", issues: [] });
    renderAt("/purchases/p1?edit=1&focus=total", { returnTo: "/needs-attention?category=milk&tag=kid" });

    await screen.findByLabelText("Итог чека");
    fireEvent.click(save());
    expect(await screen.findByRole("heading", { name: "Queue ?category=milk&tag=kid" })).toBeInTheDocument();
  });

  it("keeps the way back to the queue when editing starts from the purchase view, and stays on cancel", async () => {
    vi.mocked(updatePurchase).mockResolvedValue({ ...purchase, updatedAt: "2026-07-02T00:00:00Z", issues: [] });
    renderAt("/purchases/p1", { returnTo: "/needs-attention" });

    await screen.findByRole("heading", { level: 1, name: "Żabka" });
    expect(screen.getByRole("link", { name: /Нужно проверить/ })).toHaveAttribute("href", "/needs-attention");
    fireEvent.click(screen.getByRole("button", { name: "Исправить" }));
    fireEvent.click(await screen.findByRole("button", { name: "Отмена" }));
    fireEvent.click(await screen.findByRole("button", { name: "Исправить" }));
    await screen.findByLabelText("Итог чека");
    fireEvent.click(save());
    expect(await screen.findByRole("heading", { name: /^Queue/ })).toBeInTheDocument();
  });

  it("ignores a return address that is not the review queue", async () => {
    vi.mocked(updatePurchase).mockResolvedValue({ ...purchase, updatedAt: "2026-07-02T00:00:00Z", issues: [] });
    renderAt("/purchases/p1?edit=1", { returnTo: "https://example.com/needs-attention" });

    await screen.findByLabelText("Итог чека");
    fireEvent.click(save());
    expect(await screen.findByText("Исправления сохранены.")).toBeInTheDocument();
  });

  it("opens the product choice for a pending line and links the chosen candidate", async () => {
    vi.mocked(updatePurchase).mockResolvedValue(purchase);
    renderAt("/purchases/p1?edit=1&focus=product-1");

    const picker = await screen.findByRole("heading", { name: "Выбрать товар для строки 2" });
    fireEvent.click(within(picker.closest("[role=dialog]") as HTMLElement).getByRole("button", { name: "Выбрать «Ser Gouda 150g»" }));

    const sheet = dialog("Строка 2");
    expect(within(sheet).getByText("Связано с «Ser Gouda 150g»")).toBeInTheDocument();
    fireEvent.change(within(sheet).getByLabelText("Цена"), { target: { value: "3" } });
    fireEvent.click(within(sheet).getByRole("button", { name: "Готово" }));
    expect(within(lineRow("Ser Gouda 250g")).getByText("Связано с «Ser Gouda 150g»")).toBeInTheDocument();
    fireEvent.click(save());

    await waitFor(() => expect(updatePurchase).toHaveBeenCalled());
    expect(vi.mocked(updatePurchase).mock.calls[0][1].items[1]).toMatchObject({ unitPrice: 3, productLink: "existing", productId: "gouda150" });
  });

  it("opens the flagged line with its price field from a missing-price link", async () => {
    renderAt("/purchases/p1?edit=1&focus=price-1");
    const sheet = await screen.findByRole("heading", { name: "Строка 2" });
    const price = within(sheet.closest("[role=dialog]") as HTMLElement).getByLabelText("Цена");
    await waitFor(() => expect(price).toHaveFocus());
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
    fireEvent.click(within(dialog("Строка 2")).getByRole("button", { name: "Готово" }));

    fireEvent.click(lineRow("KAWA"));
    fireEvent.click(within(dialog("Строка 1")).getByRole("button", { name: "Выбрать товар для строки 1" }));
    fireEvent.click(within(dialog("Выбрать товар для строки 1")).getByRole("button", { name: "Создать новый товар" }));
    expect(within(dialog("Строка 1")).getByText("Будет создан новый товар «KAWA»")).toBeInTheDocument();
    fireEvent.click(within(dialog("Строка 1")).getByRole("button", { name: "Готово" }));

    fireEvent.click(save());
    await waitFor(() => expect(updatePurchase).toHaveBeenCalled());
    const items = vi.mocked(updatePurchase).mock.calls[0][1].items;
    expect(items[0]).toMatchObject({ productLink: "new", productId: null });
    expect(items[1]).toMatchObject({ productLink: "existing", productId: "cheese" });
  });

  it("adds and removes lines, blocks an unnamed line and cancels without saving", async () => {
    renderAt("/purchases/p1?edit=1");

    fireEvent.click(await screen.findByRole("button", { name: "Удалить строку 2" }));
    expect(screen.queryByRole("button", { name: /^Ser Gouda 250g/ })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Добавить строку" }));
    // A new line opens straight away, ready for its name.
    expect(within(dialog("Строка 2")).getByLabelText("Название")).toHaveValue("");
    fireEvent.click(within(dialog("Строка 2")).getByRole("button", { name: "Готово" }));
    expect(screen.getByRole("button", { name: /^Без названия/ })).toBeInTheDocument();

    fireEvent.click(save());
    expect(await screen.findByRole("alert")).toHaveTextContent("У каждой строки должно быть название.");
    expect(updatePurchase).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: "Отмена" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Żabka" })).toBeInTheDocument();
    expect(screen.getByText("Ser Gouda 250g")).toBeInTheDocument();
    expect(updatePurchase).not.toHaveBeenCalled();
  });

  it("adds a discount line that is sent as a negative amount", async () => {
    vi.mocked(updatePurchase).mockResolvedValue(purchase);
    renderAt("/purchases/p1?edit=1");

    fireEvent.click(await screen.findByRole("button", { name: "Добавить скидку" }));
    const sheet = dialog("Скидка, строка 3");
    fireEvent.change(within(sheet).getByLabelText("Название скидки"), { target: { value: "RABAT" } });
    fireEvent.change(within(sheet).getByLabelText("Размер скидки"), { target: { value: "2" } });
    fireEvent.click(within(sheet).getByRole("button", { name: "Готово" }));
    fireEvent.click(save());

    await waitFor(() => expect(updatePurchase).toHaveBeenCalled());
    expect(vi.mocked(updatePurchase).mock.calls[0][1].items[2]).toMatchObject({ name: "RABAT", kind: "discount", lineTotal: -2, productLink: "auto" });
  });

  it("explains a conflict and reloads, and shows server validation errors", async () => {
    vi.mocked(updatePurchase)
      .mockRejectedValueOnce(new ApiError("Currency must be a three-letter ISO 4217 code.", 400))
      .mockRejectedValueOnce(new ApiError("changed", 409));
    renderAt("/purchases/p1?edit=1");

    await screen.findByLabelText("Итог чека");
    fireEvent.click(save());
    expect(await screen.findByRole("alert")).toHaveTextContent("Не удалось сохранить: Currency must be a three-letter ISO 4217 code.");

    fireEvent.click(save());
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

describe("PurchaseDetailPage lines on the receipt photo", () => {
  const box = { x: 0.1, y: 0.3, width: 0.8, height: 0.04 };
  const withPhoto: PurchaseDetail = {
    ...purchase,
    sourceReceiptId: "r1",
    items: [{ ...purchase.items[0], boundingBox: box }, purchase.items[1]]
  };
  const receiptWith = (parts: number) => ({
    id: "r1",
    imageParts: Array.from({ length: parts }, (_, position) => ({ position, contentType: "image/jpeg", sizeBytes: 1, sourceImageUrl: `/api/receipts/r1/images/${position}` }))
  }) as unknown as ReceiptView;

  beforeEach(() => {
    vi.mocked(getPurchase).mockReset().mockResolvedValue(withPhoto);
    vi.mocked(getReceipt).mockReset().mockResolvedValue(receiptWith(1));
    vi.mocked(fetchProtectedBlob).mockReset().mockResolvedValue(new Blob(["x"], { type: "image/jpeg" }));
    // jsdom has no object URLs.
    URL.createObjectURL = vi.fn(() => "blob:receipt-photo");
    URL.revokeObjectURL = vi.fn();
  });

  it("opens the tapped line outlined on the photo", async () => {
    renderAt("/purchases/p1");

    const line = await screen.findByRole("button", { name: /^KAWA.*Показать на фото$/ });
    // The «Оценить» button stays its own control, outside the line button.
    expect(within(line).queryByRole("button")).toBeNull();
    await waitFor(() => expect(getReceipt).toHaveBeenCalledWith("r1"));
    fireEvent.click(line);

    const viewer = dialog();
    expect(within(viewer).getByRole("heading", { level: 2 })).toHaveTextContent("Строка 1 на фото");
    expect(within(viewer).getByRole("img", { name: "Фрагмент фото чека, рамкой отмечена строка «KAWA»" })).toHaveAttribute("src", "blob:receipt-photo");
  });

  it("shows a line without a box on the whole photo", async () => {
    renderAt("/purchases/p1");
    await waitFor(() => expect(getReceipt).toHaveBeenCalled());
    fireEvent.click(await screen.findByRole("button", { name: /^Ser Gouda 250g.*Показать на фото$/ }));

    expect(within(dialog()).getByRole("img", { name: "Фото чека целиком" })).toBeInTheDocument();
    expect(within(dialog()).getByText(/не найдено — показан весь чек/)).toBeInTheDocument();
  });

  it("does not outline lines of a multi-photo receipt", async () => {
    vi.mocked(getReceipt).mockResolvedValue(receiptWith(2));
    renderAt("/purchases/p1");
    await waitFor(() => expect(getReceipt).toHaveBeenCalled());
    fireEvent.click(await screen.findByRole("button", { name: /^KAWA.*Показать на фото$/ }));

    expect(await within(dialog()).findByText(/нескольких фото/)).toBeInTheDocument();
    expect(within(dialog()).getByRole("img", { name: "Фото чека целиком" })).toBeInTheDocument();
  });

  it("offers «Показать на фото» inside a line in the editor", async () => {
    renderAt("/purchases/p1?edit=1");

    fireEvent.click(await screen.findByRole("button", { name: /^KAWA/ }));
    fireEvent.click(await within(dialog("Строка 1")).findByRole("button", { name: "Показать на фото" }));
    expect(within(dialog()).getByRole("heading", { level: 2 })).toHaveTextContent("Строка 1 на фото");
  });

  it("names tappable discount rows like product lines", async () => {
    vi.mocked(getPurchase).mockResolvedValue({
      ...withPhoto,
      items: [...withPhoto.items, {
        ...purchase.items[0], id: "line-rabat", position: 2, name: "RABAT", quantity: null, unit: null, unitPrice: null,
        lineTotal: -1, kind: "discount", productId: null, productName: null, boundingBox: null
      }]
    });
    renderAt("/purchases/p1");
    fireEvent.click(await screen.findByRole("button", { name: /RABAT.*Показать на фото$/ }));
    expect(within(dialog()).getByRole("heading", { level: 2 })).toHaveTextContent("Строка 3 на фото");
  });

  it("keeps lines of a purchase without a photo plain", async () => {
    vi.mocked(getPurchase).mockResolvedValue(purchase);
    renderAt("/purchases/p1");
    expect(await screen.findByRole("heading", { level: 3, name: "KAWA" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Показать на фото/ })).not.toBeInTheDocument();
    expect(getReceipt).not.toHaveBeenCalled();
  });
});
