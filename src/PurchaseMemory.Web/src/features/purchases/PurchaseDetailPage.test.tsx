import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError, fetchProtectedBlob, getProduct, getProductCatalog, getPurchase, getReceipt, postLineApprovals, updatePurchase } from "../../api/client";
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
    getProduct: vi.fn(),
    listPurchaseImpressions: vi.fn(() => Promise.resolve([])),
    createImpression: vi.fn(),
    deleteImpression: vi.fn(),
    fetchProtectedBlob: vi.fn(() => new Promise(() => undefined)),
    getReceipt: vi.fn(),
    postLineApprovals: vi.fn()
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
      discountedItemPosition: null, productId: "coffee", productName: "Kawa", productNeedsReview: false, boundingBox: null
    },
    {
      id: "line-gouda", position: 1, name: "Ser Gouda 250g", quantity: 1, unit: "szt", unitPrice: null, lineTotal: 3, kind: "product",
      // A doubtful match: the line got a product of its own, flagged «проверить».
      discountedItemPosition: null, productId: "gouda250", productName: "Ser Gouda 250g", productNeedsReview: true, boundingBox: null
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
  return render(
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
    vi.mocked(getProduct).mockReset();
  });

  it("shows the saved purchase with its issues and switches to edit mode", async () => {
    renderAt("/purchases/p1");

    expect(await screen.findByRole("heading", { level: 1, name: "Żabka" })).toBeInTheDocument();
    expect(screen.getByText("Связано с «Kawa»")).toBeInTheDocument();
    expect(screen.getByText("Связано с «Ser Gouda 250g». Товар отмечен «проверить»: он похож на другие товары")).toBeInTheDocument();
    expect(screen.getByText("проверить")).toBeInTheDocument();
    expect(screen.getByText("Нет цены")).toBeInTheDocument();
    expect(screen.getByText("Сумма строк не сходится с итогом чека")).toBeInTheDocument();
    // No photo, no photo section (and no sentence about it).
    expect(screen.queryByRole("region", { name: "Фото чека" })).not.toBeInTheDocument();

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
    expect(body.items[1]).toMatchObject({ name: "Ser Gouda 250g", productLink: "existing", productId: "gouda250" });
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

  it("opens the product choice for a flagged line with its similar products and links one", async () => {
    vi.mocked(getProduct).mockResolvedValue({
      id: "gouda250", name: "Ser Gouda 250g", needsReview: true,
      reviewCandidates: [{ productId: "gouda150", name: "Ser Gouda 150g", brand: null, categoryId: null, purchaseCount: 4, score: 0.8 }]
    } as never);
    vi.mocked(updatePurchase).mockResolvedValue(purchase);
    renderAt("/purchases/p1?edit=1&focus=product-1");

    const picker = await screen.findByRole("heading", { name: "Выбрать товар для строки 2" });
    const pickerDialog = picker.closest("[role=dialog]") as HTMLElement;
    // Before typing: the matcher's candidates of the flagged product.
    expect(await within(pickerDialog).findByRole("heading", { name: "Похожие товары" })).toBeInTheDocument();
    expect(getProduct).toHaveBeenCalledWith("gouda250");
    expect(getProductCatalog).not.toHaveBeenCalled();
    fireEvent.click(within(pickerDialog).getByRole("button", { name: "Выбрать «Ser Gouda 150g»" }));

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
      page: 1, pageSize: 8, totalCount: 1, hasMore: false, needsReviewCount: 0
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

describe("PurchaseDetailPage line review (carousel)", () => {
  const box = { x: 0.1, y: 0.3, width: 0.8, height: 0.04 };
  const withPhoto: PurchaseDetail = {
    ...purchase,
    sourceReceiptId: "r1",
    items: [{ ...purchase.items[0], boundingBox: box, approved: false }, { ...purchase.items[1], approved: false }]
  };
  const receiptWith = (parts: number) => ({
    id: "r1",
    imageParts: Array.from({ length: parts }, (_, position) => ({ position, contentType: "image/jpeg", sizeBytes: 1, sourceImageUrl: `/api/receipts/r1/images/${position}` }))
  }) as unknown as ReceiptView;
  const card = () => screen.getByTestId("line-carousel-card");
  const shownName = () => (within(dialog()).getByLabelText(/^Название/) as HTMLInputElement).value;

  beforeEach(() => {
    vi.mocked(getPurchase).mockReset().mockResolvedValue(withPhoto);
    vi.mocked(getReceipt).mockReset().mockResolvedValue(receiptWith(1));
    vi.mocked(fetchProtectedBlob).mockReset().mockResolvedValue(new Blob(["x"], { type: "image/jpeg" }));
    vi.mocked(postLineApprovals).mockReset().mockImplementation(async (items) => ({
      items: items.map((item) => ({ ...item, status: "applied" as const, approvedAt: item.at }))
    }));
    vi.mocked(updatePurchase).mockReset();
    // jsdom has no object URLs.
    URL.createObjectURL = vi.fn(() => "blob:receipt-photo");
    URL.revokeObjectURL = vi.fn();
  });

  it("tapping a line opens the review right at it, highlighted on the photo, editable in place", async () => {
    renderAt("/purchases/p1");

    const line = await screen.findByRole("button", { name: /^KAWA.*сверить с чеком$/ });
    // The «Оценить» button stays its own control, outside the line button.
    expect(within(line).queryByRole("button")).toBeNull();
    await waitFor(() => expect(getReceipt).toHaveBeenCalledWith("r1"));
    fireEvent.click(line);

    const review = dialog();
    expect(within(review).getByLabelText("Проверено 0 из 2")).toBeInTheDocument();
    expect(within(review).getByText("Товар 1 из 2")).toBeInTheDocument();
    expect(shownName()).toBe("KAWA");
    expect(within(review).getByLabelText("Цена")).toHaveValue("15");
    expect(within(review).getByText("Связано с «Kawa»")).toBeInTheDocument();
    expect(await within(review).findByRole("img", { name: "Фото чека, выделена строка «KAWA»" })).toHaveAttribute("src", "blob:receipt-photo");
    // Lines add to 18 against a total of 20.
    expect(within(review).getByText(/^Строки дают 18,00.* из 20,00.* · не хватает 2,00/)).toBeInTheDocument();
  });

  it("shows a line without a box on the whole photo", async () => {
    renderAt("/purchases/p1");
    await waitFor(() => expect(getReceipt).toHaveBeenCalled());
    fireEvent.click(await screen.findByRole("button", { name: /^Ser Gouda 250g.*сверить с чеком$/ }));
    expect(await within(dialog()).findByRole("img", { name: "Фото чека целиком" })).toBeInTheDocument();
    expect(within(dialog()).getByText(/не найдено — показан весь чек/)).toBeInTheDocument();
  });

  it("does not highlight lines of a multi-photo receipt", async () => {
    vi.mocked(getReceipt).mockResolvedValue(receiptWith(2));
    renderAt("/purchases/p1");
    await waitFor(() => expect(getReceipt).toHaveBeenCalled());
    fireEvent.click(await screen.findByRole("button", { name: /^KAWA.*сверить с чеком$/ }));

    expect(await within(dialog()).findByText(/нескольких фото/)).toBeInTheDocument();
    expect(within(dialog()).getByRole("img", { name: "Фото чека целиком" })).toBeInTheDocument();
  });

  it("«Верно, дальше» checks the line: ✓ in the list at once, sent to the server in a batch when the app is hidden", async () => {
    renderAt("/purchases/p1");
    fireEvent.click(await screen.findByRole("button", { name: /^KAWA.*сверить с чеком$/ }));

    fireEvent.click(within(dialog()).getByRole("button", { name: "Верно, дальше" }));

    await waitFor(() => expect(shownName()).toBe("Ser Gouda 250g"));
    expect(screen.getByRole("button", { name: /^KAWA, проверено/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Проверить строки: проверено 1 из 2/ })).toBeInTheDocument();
    // Not a request per mark: nothing is sent until the batch is due.
    expect(postLineApprovals).not.toHaveBeenCalled();
    expect(updatePurchase).not.toHaveBeenCalled();

    Object.defineProperty(document, "visibilityState", { value: "hidden", configurable: true });
    document.dispatchEvent(new Event("visibilitychange"));
    Object.defineProperty(document, "visibilityState", { value: "visible", configurable: true });
    await waitFor(() => expect(postLineApprovals).toHaveBeenCalledTimes(1));
    expect(vi.mocked(postLineApprovals).mock.calls[0]).toEqual([
      [expect.objectContaining({ purchaseId: "p1", lineId: "line-kawa", approved: true })],
      { keepalive: true }
    ]);
  });

  it("a swipe moves between lines without checking them", async () => {
    renderAt("/purchases/p1");
    fireEvent.click(await screen.findByRole("button", { name: /^KAWA.*сверить с чеком$/ }));

    const element = card();
    fireEvent.pointerDown(element, { pointerId: 1, clientX: 300, clientY: 300 });
    fireEvent.pointerMove(element, { pointerId: 1, clientX: 250, clientY: 300 });
    fireEvent.pointerUp(element, { pointerId: 1, clientX: 200, clientY: 300 });

    expect(shownName()).toBe("Ser Gouda 250g");
    expect(screen.queryByRole("button", { name: /^KAWA, проверено/ })).not.toBeInTheDocument();
  });

  it("the header button starts at the first line with a problem; checked lines from the server show ✓", async () => {
    vi.mocked(getPurchase).mockResolvedValue({ ...withPhoto, items: [{ ...withPhoto.items[0], approved: true }, withPhoto.items[1]] });
    renderAt("/purchases/p1");

    expect(await screen.findByRole("button", { name: /^KAWA, проверено/ })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Проверить строки: проверено 1 из 2/ }));
    expect(shownName()).toBe("Ser Gouda 250g");
    // Gouda has no price: that is its problem, and the dot of the first line shows it is checked.
    expect(within(dialog()).getByText("Цена не распознана — впишите её по фото.")).toBeInTheDocument();
    expect(within(dialog()).getByRole("button", { name: "Товар 1, проверен" })).toBeInTheDocument();
  });

  it("a corrected line is saved when the person moves on, and «Верно, дальше» on it counts as checked", async () => {
    const corrected: PurchaseDetail = {
      ...withPhoto,
      updatedAt: "2026-07-02T00:00:00Z",
      items: [{ ...withPhoto.items[0], id: "line-kawa-2", unitPrice: 14, lineTotal: 14 }, { ...withPhoto.items[1], id: "line-gouda-2" }]
    };
    vi.mocked(updatePurchase).mockResolvedValue(corrected);
    renderAt("/purchases/p1");
    fireEvent.click(await screen.findByRole("button", { name: /^KAWA.*сверить с чеком$/ }));

    fireEvent.change(within(dialog()).getByLabelText("Цена"), { target: { value: "14" } });
    fireEvent.change(within(dialog()).getByLabelText("Сумма"), { target: { value: "14" } });
    expect(updatePurchase).not.toHaveBeenCalled();
    fireEvent.click(within(dialog()).getByRole("button", { name: "Верно, дальше" }));

    await waitFor(() => expect(updatePurchase).toHaveBeenCalledTimes(1));
    const [id, body] = vi.mocked(updatePurchase).mock.calls[0];
    expect(id).toBe("p1");
    expect(body.expectedUpdatedAt).toBe(withPhoto.updatedAt);
    // The corrected line and the untouched one, each with the id it continues.
    expect(body.items[0]).toMatchObject({ id: "line-kawa", name: "KAWA", unitPrice: 14, lineTotal: 14, productLink: "existing", productId: "coffee", boundingBox: box });
    expect(body.items[1]).toMatchObject({ id: "line-gouda", name: "Ser Gouda 250g", lineTotal: 3 });

    await waitFor(() => expect(shownName()).toBe("Ser Gouda 250g"));
    expect(screen.getByRole("button", { name: /^KAWA, проверено/ })).toBeInTheDocument();
  });

  it("correcting only a product's discount and pressing «Верно» checks the product and its discount", async () => {
    const withDiscount: PurchaseDetail = {
      ...withPhoto,
      items: [...withPhoto.items, {
        ...purchase.items[0], id: "line-rabat", position: 2, name: "RABAT", quantity: null, unit: null, unitPrice: null,
        lineTotal: -1, kind: "discount", productId: null, productName: null, boundingBox: null
      }]
    };
    vi.mocked(getPurchase).mockResolvedValue(withDiscount);
    // The saved lines come back with new ids, as after a real save.
    vi.mocked(updatePurchase).mockResolvedValue({
      ...withDiscount,
      updatedAt: "2026-07-02T00:00:00Z",
      items: withDiscount.items.map((item) => ({ ...item, id: `${item.id}-2`, lineTotal: item.kind === "discount" ? -0.5 : item.lineTotal }))
    });
    renderAt("/purchases/p1");
    fireEvent.click(await screen.findByRole("button", { name: /^Ser Gouda.*сверить с чеком$/ }));

    fireEvent.click(within(dialog()).getByRole("button", { name: /^Скидка −1,00/ }));
    fireEvent.change(within(dialog()).getByLabelText("Размер скидки"), { target: { value: "0,5" } });
    fireEvent.click(within(dialog()).getByRole("button", { name: "Верно" }));

    await waitFor(() => expect(updatePurchase).toHaveBeenCalledTimes(1));
    expect(vi.mocked(updatePurchase).mock.calls[0][1].items[2]).toMatchObject({ id: "line-rabat", lineTotal: -0.5 });
    expect(await within(dialog()).findByLabelText("Проверено 2 из 3")).toBeInTheDocument();
    expect(within(dialog()).getByRole("button", { name: "Товар 2, проверен" })).toBeInTheDocument();
  });

  it("closing the review with Escape saves the corrected line first", async () => {
    vi.mocked(updatePurchase).mockResolvedValue(withPhoto);
    renderAt("/purchases/p1");
    fireEvent.click(await screen.findByRole("button", { name: /^KAWA.*сверить с чеком$/ }));

    fireEvent.change(within(dialog()).getByLabelText("Название"), { target: { value: "KAWA MIELONA" } });
    fireEvent.keyDown(dialog(), { key: "Escape" });

    await waitFor(() => expect(updatePurchase).toHaveBeenCalledTimes(1));
    expect(vi.mocked(updatePurchase).mock.calls[0][1].items[0]).toMatchObject({ id: "line-kawa", name: "KAWA MIELONA" });
    await waitFor(() => expect(screen.queryByTestId("line-carousel-card")).not.toBeInTheDocument());
  });

  it("a failed save keeps the review open on Escape", async () => {
    vi.mocked(updatePurchase).mockRejectedValue(new Error("Нет связи"));
    renderAt("/purchases/p1");
    fireEvent.click(await screen.findByRole("button", { name: /^KAWA.*сверить с чеком$/ }));

    fireEvent.change(within(dialog()).getByLabelText("Сумма"), { target: { value: "16" } });
    fireEvent.keyDown(dialog(), { key: "Escape" });

    expect(await within(dialog()).findByRole("alert")).toHaveTextContent("Нет связи");
    expect(screen.getByTestId("line-carousel-card")).toBeInTheDocument();
  });

  it("leaving the page with the review open still sends the corrected line", async () => {
    vi.mocked(updatePurchase).mockResolvedValue(withPhoto);
    const { unmount } = renderAt("/purchases/p1");
    fireEvent.click(await screen.findByRole("button", { name: /^KAWA.*сверить с чеком$/ }));

    fireEvent.change(within(dialog()).getByLabelText("Цена"), { target: { value: "14" } });
    unmount();

    await waitFor(() => expect(updatePurchase).toHaveBeenCalledTimes(1));
    expect(vi.mocked(updatePurchase).mock.calls[0][1].items[0]).toMatchObject({ id: "line-kawa", unitPrice: 14 });
  });

  it("while a corrected line is being saved its fields are read-only, so nothing typed meanwhile is lost", async () => {
    let finish: (value: PurchaseDetail) => void = () => undefined;
    vi.mocked(updatePurchase).mockImplementation(() => new Promise<PurchaseDetail>((resolve) => { finish = resolve; }));
    renderAt("/purchases/p1");
    fireEvent.click(await screen.findByRole("button", { name: /^KAWA.*сверить с чеком$/ }));

    fireEvent.change(within(dialog()).getByLabelText("Название"), { target: { value: "KAWA ZIARNISTA" } });
    fireEvent.click(within(dialog()).getByRole("button", { name: "Следующая строка" }));
    await waitFor(() => expect(updatePurchase).toHaveBeenCalledTimes(1));

    const name = within(dialog()).getByLabelText("Название");
    expect(name).toHaveAttribute("readonly");
    expect(within(dialog()).getByLabelText("Цена")).toHaveAttribute("readonly");
    // A change that arrives anyway is not taken (it would vanish when the save completes).
    fireEvent.change(name, { target: { value: "KAWA ZIARNISTA 1KG" } });
    expect(name).toHaveValue("KAWA ZIARNISTA");

    finish({ ...withPhoto, updatedAt: "2026-07-02T00:00:00Z", items: [{ ...withPhoto.items[0], name: "KAWA ZIARNISTA" }, withPhoto.items[1]] });
    await waitFor(() => expect(within(dialog()).getByText("Товар 2 из 2")).toBeInTheDocument());
    expect(within(dialog()).getByLabelText("Название")).not.toHaveAttribute("readonly");
    expect(updatePurchase).toHaveBeenCalledTimes(1);
  });

  it("leaving a corrected line with the chevron saves it too", async () => {
    vi.mocked(updatePurchase).mockResolvedValue(withPhoto);
    renderAt("/purchases/p1");
    fireEvent.click(await screen.findByRole("button", { name: /^KAWA.*сверить с чеком$/ }));

    fireEvent.change(within(dialog()).getByLabelText("Название"), { target: { value: "KAWA ZIARNISTA" } });
    fireEvent.click(within(dialog()).getByRole("button", { name: "Следующая строка" }));

    await waitFor(() => expect(updatePurchase).toHaveBeenCalledTimes(1));
    expect(vi.mocked(updatePurchase).mock.calls[0][1].items[0]).toMatchObject({ id: "line-kawa", name: "KAWA ZIARNISTA" });
    await waitFor(() => expect(within(dialog()).getByText("Товар 2 из 2")).toBeInTheDocument());
  });

  it("changes the line's product right under the line, saved with it", async () => {
    vi.mocked(getProductCatalog).mockResolvedValue({
      items: [{ id: "gouda150", name: "Ser Gouda 150g", normalizedName: "ser gouda 150 g", purchaseCount: 4, linkedItemCount: 4, aliasCount: 1, createdAt: "", updatedAt: "" }],
      page: 1, pageSize: 8, totalCount: 1, hasMore: false, needsReviewCount: 1
    } as never);
    vi.mocked(updatePurchase).mockResolvedValue(withPhoto);
    renderAt("/purchases/p1");
    fireEvent.click(await screen.findByRole("button", { name: /^Ser Gouda 250g.*сверить с чеком$/ }));
    expect(within(dialog()).getByText("Связано с «Ser Gouda 250g». Товар отмечен «проверить»: он похож на другие товары")).toBeInTheDocument();

    fireEvent.click(within(dialog()).getByRole("button", { name: "Выбрать товар для строки 2" }));
    const picker = dialog("Выбрать товар для строки 2");
    fireEvent.change(within(picker).getByLabelText("Найти товар"), { target: { value: "gouda" } });
    fireEvent.click(await within(picker).findByRole("button", { name: "Выбрать «Ser Gouda 150g»" }));

    const review = screen.getAllByRole("dialog").find((element) => within(element).queryByTestId("line-carousel-card"))!;
    expect(within(review).getByText("Связано с «Ser Gouda 150g»")).toBeInTheDocument();
    // Photos and barcodes wait for the save: the new product is not the line's yet.
    expect(within(review).getByText(/Товар строки изменится после сохранения/)).toBeInTheDocument();
    fireEvent.change(within(review).getByLabelText("Цена"), { target: { value: "3" } });
    fireEvent.click(within(review).getByRole("button", { name: "Верно" }));

    await waitFor(() => expect(updatePurchase).toHaveBeenCalledTimes(1));
    const body = vi.mocked(updatePurchase).mock.calls[0][1];
    expect(body.items[1]).toMatchObject({ id: "line-gouda", productLink: "existing", productId: "gouda150", unitPrice: 3 });
    expect(body.items[0]).toMatchObject({ productLink: "existing", productId: "coffee" });
  });

  it("explains a conflict while saving a corrected line and stays on it", async () => {
    vi.mocked(updatePurchase).mockRejectedValue(new ApiError("changed", 409));
    renderAt("/purchases/p1");
    fireEvent.click(await screen.findByRole("button", { name: /^KAWA.*сверить с чеком$/ }));
    fireEvent.change(within(dialog()).getByLabelText("Сумма"), { target: { value: "16" } });
    fireEvent.click(within(dialog()).getByRole("button", { name: "Верно, дальше" }));

    expect(await within(dialog()).findByRole("alert")).toHaveTextContent(/только что изменили в другом окне/);
    expect(shownName()).toBe("KAWA");
    await waitFor(() => expect(getPurchase).toHaveBeenCalledTimes(2));
  });

  it("«Удалить» saves the purchase without the line after confirmation", async () => {
    vi.mocked(updatePurchase).mockResolvedValue({ ...withPhoto, items: [withPhoto.items[0]] });
    renderAt("/purchases/p1");
    fireEvent.click(await screen.findByRole("button", { name: /^Ser Gouda 250g.*сверить с чеком$/ }));

    fireEvent.click(within(dialog()).getByRole("button", { name: "Удалить" }));
    fireEvent.click(within(dialog("Удалить строку 2?")).getByRole("button", { name: "Удалить" }));

    await waitFor(() => expect(updatePurchase).toHaveBeenCalledTimes(1));
    expect(vi.mocked(updatePurchase).mock.calls[0][1].items.map((item) => item.id)).toEqual(["line-kawa"]);
    await waitFor(() => expect(shownName()).toBe("KAWA"));
  });

  it("the purchase editor sends each line's id so checked marks survive a correction", async () => {
    vi.mocked(updatePurchase).mockResolvedValue(withPhoto);
    renderAt("/purchases/p1?edit=1");
    fireEvent.click(await screen.findByRole("button", { name: "Сохранить" }));
    await waitFor(() => expect(updatePurchase).toHaveBeenCalled());
    expect(vi.mocked(updatePurchase).mock.calls[0][1].items.map((item) => item.id)).toEqual(["line-kawa", "line-gouda"]);
  });

  it("shows the line on the photo at the top of its sheet in the editor", async () => {
    renderAt("/purchases/p1?edit=1");

    fireEvent.click(await screen.findByRole("button", { name: /^KAWA/ }));
    expect(await within(dialog("Строка 1")).findByRole("img", { name: "Фото чека, выделена строка «KAWA»" })).toBeInTheDocument();
    expect(within(dialog("Строка 1")).queryByRole("button", { name: "Показать на фото" })).not.toBeInTheDocument();
  });

  it("opens a discount row in the review too", async () => {
    vi.mocked(getPurchase).mockResolvedValue({
      ...withPhoto,
      items: [...withPhoto.items, {
        ...purchase.items[0], id: "line-rabat", position: 2, name: "RABAT", quantity: null, unit: null, unitPrice: null,
        lineTotal: -1, kind: "discount", productId: null, productName: null, boundingBox: null
      }]
    });
    vi.mocked(updatePurchase).mockResolvedValue({ ...withPhoto, updatedAt: "2026-07-02T00:00:00Z", issues: [] });
    renderAt("/purchases/p1");
    fireEvent.click(await screen.findByRole("button", { name: /RABAT.*сверить с чеком$/ }));
    // The discount is inside its product's card, which counts products only.
    expect(within(dialog()).getByText("Товар 2 из 2")).toBeInTheDocument();
    expect(within(dialog()).getByLabelText("Название")).toHaveValue("Ser Gouda 250g");
    fireEvent.click(within(dialog()).getByRole("button", { name: /^Скидка −1,00.* · к оплате 2,00/ }));
    expect(within(dialog()).getByLabelText("Название скидки")).toHaveValue("RABAT");
    expect(within(dialog()).getByLabelText("Размер скидки")).toHaveValue("1");

    // «Убрать» asks first: the purchase is saved without the discount at once.
    fireEvent.click(within(dialog()).getByRole("button", { name: "Убрать скидку RABAT" }));
    fireEvent.click(within(dialog("Убрать скидку?")).getByRole("button", { name: "Убрать" }));
    await waitFor(() => expect(updatePurchase).toHaveBeenCalledTimes(1));
    expect(vi.mocked(updatePurchase).mock.calls[0][1].items.map((item) => item.name)).toEqual(["KAWA", "Ser Gouda 250g"]);
  });

  it("reviews the lines of a purchase without a photo too, without asking for a receipt", async () => {
    vi.mocked(getPurchase).mockResolvedValue(purchase);
    renderAt("/purchases/p1");
    fireEvent.click(await screen.findByRole("button", { name: /^KAWA.*сверить с чеком$/ }));
    expect(within(dialog()).getByText("У этой покупки нет фото чека.")).toBeInTheDocument();
    expect(getReceipt).not.toHaveBeenCalled();
  });
});
