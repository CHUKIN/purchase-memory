import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, useLocation } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { classifyProduct, getPurchase, getReviewQueue, listCategories, listTags, updatePurchase } from "../../api/client";
import type { PurchaseDetail, PurchaseDetailItem, ReviewQueue, ReviewQueueItem, TagSummary } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { sampleCategories } from "../categories/categoryFixtures";
import { findRow, openFilters, pick, row } from "../../test/ionicHelpers";
import { ReviewQueuePage } from "./ReviewQueuePage";

vi.mock("../../api/client", () => ({
  getReviewQueue: vi.fn(),
  classifyProduct: vi.fn(),
  listTags: vi.fn(),
  listCategories: vi.fn(),
  getPurchase: vi.fn(),
  updatePurchase: vi.fn()
}));

const householdTags: TagSummary[] = [
  { id: "kid", name: "Для ребёнка", productCount: 2 },
  { id: "promo", name: "Акция", productCount: 5 }
];

function item(overrides: Partial<ReviewQueueItem> & Pick<ReviewQueueItem, "kind" | "id">): ReviewQueueItem {
  return {
    title: "",
    occurredAt: "2026-09-20T10:00:00Z",
    receiptId: null,
    purchaseId: null,
    productId: null,
    categoryId: null,
    categoryName: null,
    currency: null,
    receiptTotal: null,
    linesTotal: null,
    affectedLineCount: null,
    ...overrides
  };
}

const queue: ReviewQueue = {
  totalCount: 4,
  groups: [
    {
      kind: "totalMismatch",
      totalCount: 2,
      items: [item({ kind: "totalMismatch", id: "totalMismatch:p1", title: "Żabka", receiptId: "r1", purchaseId: "p1", currency: "PLN", receiptTotal: 20, linesTotal: 15 })]
    },
    {
      kind: "suggestedCategory",
      totalCount: 1,
      items: [item({ kind: "suggestedCategory", id: "suggestedCategory:m1", title: "MLEKO 2%", productId: "m1", categoryId: "milk", categoryName: "Молоко" })]
    },
    {
      kind: "missingCategory",
      totalCount: 1,
      items: [item({ kind: "missingCategory", id: "missingCategory:b1", title: "CHLEB", productId: "b1" })]
    }
  ]
};

function LocationProbe() {
  const location = useLocation();
  return (
    <>
      <output data-testid="location">{location.search}</output>
      <output data-testid="pathname">{location.pathname}</output>
      <output data-testid="state">{JSON.stringify(location.state ?? null)}</output>
    </>
  );
}

function line(overrides: Partial<PurchaseDetailItem> & Pick<PurchaseDetailItem, "position" | "name">): PurchaseDetailItem {
  return {
    id: `line-${overrides.position}`,
    quantity: 1,
    unit: "szt",
    unitPrice: 5,
    lineTotal: 5,
    kind: "product",
    discountedItemPosition: null,
    productId: null,
    productName: null,
    pendingMatch: false,
    candidates: [],
    boundingBox: null,
    ...overrides
  };
}

const pendingItem = item({
  kind: "pendingMatch",
  id: "pendingMatch:m9",
  title: "Ser Gouda 250g",
  purchaseId: "p3",
  receiptId: "r3",
  productId: "g1",
  itemPosition: 1,
  candidates: [
    { productId: "g1", name: "Ser Gouda 150g", brand: "Hochland", categoryId: "cheese" },
    { productId: "g2", name: "Ser Edam", brand: null, categoryId: null }
  ]
});

const pendingQueue: ReviewQueue = { totalCount: 1, groups: [{ kind: "pendingMatch", totalCount: 1, items: [pendingItem] }] };

// The purchase as stored: a linked line, the pending line and a discount on it.
const storedPurchase: PurchaseDetail = {
  id: "p3",
  sourceReceiptId: "r3",
  merchantName: "Biedronka",
  purchasedAt: "2026-09-20T10:00:00Z",
  currency: "PLN",
  receiptTotal: 11,
  createdAt: "2026-09-20T10:00:00Z",
  updatedAt: "2026-09-20T10:05:00.123Z",
  items: [
    line({ position: 0, name: "MLEKO", productId: "milk-1", productName: "Mleko 2%", boundingBox: { x: 0.1, y: 0.2, width: 0.5, height: 0.05 } }),
    line({ position: 1, name: "Ser Gouda 250g", unitPrice: 7, lineTotal: 7, pendingMatch: true }),
    line({ position: 2, name: "RABAT", quantity: null, unit: null, unitPrice: null, lineTotal: -1, kind: "discount", discountedItemPosition: 1 })
  ],
  issues: []
};

function renderPage(path = "/needs-attention") {
  render(
    <AppProviders locale="ru">
      <MemoryRouter initialEntries={[path]}>
        <ReviewQueuePage />
        <LocationProbe />
      </MemoryRouter>
    </AppProviders>
  );
}

describe("ReviewQueuePage", () => {
  beforeEach(() => {
    vi.mocked(getReviewQueue).mockReset();
    vi.mocked(classifyProduct).mockReset();
    vi.mocked(listTags).mockReset().mockResolvedValue([]);
    vi.mocked(listCategories).mockReset().mockResolvedValue([]);
    vi.mocked(getPurchase).mockReset();
    vi.mocked(updatePurchase).mockReset();
  });

  it("groups items by kind with counts, reasons and fix links", async () => {
    vi.mocked(getReviewQueue).mockResolvedValue(queue);
    renderPage();

    expect(screen.getByRole("status", { name: "Загружаем очередь проверки" })).toBeInTheDocument();
    const mismatch = await screen.findByRole("region", { name: "Сумма не сходится" });
    expect(screen.getByRole("heading", { level: 1, name: "Нужно проверить" })).toBeInTheDocument();
    expect(screen.getByText("4 пункта требуют внимания.")).toBeInTheDocument();
    expect(within(mismatch).getByLabelText("2 пункта")).toBeInTheDocument();
    const card = within(mismatch).getByRole("article", { name: "Żabka" });
    expect(card).toHaveTextContent(/Строки дают 15,00\s?(zł|PLN), а итог чека — 20,00\s?(zł|PLN)\./);
    expect(within(card).getByRole("link", { name: "Открыть чек" })).toHaveAttribute("href", "/receipts/r1");
    expect(within(mismatch).getByText(/И ещё 1/)).toBeInTheDocument();

    const missing = screen.getByRole("region", { name: "Без категории" });
    expect(within(missing).getByRole("link", { name: "Выбрать категорию: CHLEB" })).toHaveAttribute("href", "/products/b1");
  });

  it("confirms a suggested category in one click and reloads the queue", async () => {
    vi.mocked(getReviewQueue).mockResolvedValueOnce(queue).mockResolvedValue({ totalCount: 0, groups: [] });
    vi.mocked(classifyProduct).mockResolvedValue({ productId: "m1", categoryId: "milk", categorySource: "confirmed", brand: null, brandSource: null });
    renderPage();

    const confirm = await screen.findByRole("button", { name: "Подтвердить категорию «Молоко» для «MLEKO 2%»" });
    fireEvent.click(confirm);

    await waitFor(() => expect(classifyProduct).toHaveBeenCalledWith("m1", { categoryId: "milk" }));
    expect(await screen.findByRole("heading", { name: "Всё проверено" })).toBeInTheDocument();
  });

  it("shows an error when confirming fails and keeps the item", async () => {
    vi.mocked(getReviewQueue).mockResolvedValue(queue);
    vi.mocked(classifyProduct).mockRejectedValue(new Error("boom"));
    renderPage();

    fireEvent.click(await screen.findByRole("button", { name: /Подтвердить категорию/ }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Не удалось подтвердить категорию");
    expect(screen.getByRole("article", { name: "MLEKO 2%" })).toBeInTheDocument();
  });

  it("shows the empty state", async () => {
    vi.mocked(getReviewQueue).mockResolvedValue({ totalCount: 0, groups: [] });
    renderPage();
    expect(await screen.findByRole("heading", { level: 2, name: "Всё проверено" })).toBeInTheDocument();
  });

  it("shows a load error with retry", async () => {
    vi.mocked(getReviewQueue).mockRejectedValueOnce(new Error("offline")).mockRejectedValueOnce(new Error("offline")).mockResolvedValue({ totalCount: 0, groups: [] });
    renderPage();
    expect(await screen.findByRole("alert", {}, { timeout: 4_000 })).toHaveTextContent("Не удалось загрузить очередь проверки.");
    fireEvent.click(screen.getByRole("button", { name: "Повторить" }));
    expect(await screen.findByRole("heading", { name: "Всё проверено" })).toBeInTheDocument();
  });

  it("names untitled receipts and opens a receipt-less purchase in the editor on the missing price", async () => {
    vi.mocked(getReviewQueue).mockResolvedValue({
      totalCount: 1,
      groups: [{ kind: "missingPrice", totalCount: 1, items: [item({ kind: "missingPrice", id: "missingPrice:p2", purchaseId: "p2", affectedLineCount: 3, itemPosition: 1 })] }]
    });
    renderPage();
    const card = await screen.findByRole("article", { name: "Магазин не распознан" });
    expect(card).toHaveTextContent("3 строки без цены.");
    expect(within(card).getByRole("link", { name: "Исправить покупку «Магазин не распознан»" })).toHaveAttribute("href", "/purchases/p2?edit=1&focus=price-1");
    expect(within(card).queryByRole("link", { name: "Открыть чек" })).not.toBeInTheDocument();
  });

  it("deep-links a total mismatch and a pending match into the purchase editor", async () => {
    vi.mocked(getReviewQueue).mockResolvedValue({
      totalCount: 2,
      groups: [
        queue.groups[0],
        { kind: "pendingMatch", totalCount: 1, items: [item({ kind: "pendingMatch", id: "pendingMatch:m9", title: "Ser Gouda 250g", purchaseId: "p3", receiptId: "r3", productId: "g1", itemPosition: 2 })] }
      ]
    });
    renderPage();
    const mismatch = await screen.findByRole("article", { name: "Żabka" });
    expect(within(mismatch).getByRole("link", { name: "Исправить покупку «Żabka»" })).toHaveAttribute("href", "/purchases/p1?edit=1&focus=total");
    const pending = screen.getByRole("article", { name: "Ser Gouda 250g" });
    expect(within(pending).getByRole("link", { name: "Выбрать товар для «Ser Gouda 250g»" })).toHaveAttribute("href", "/purchases/p3?edit=1&focus=product-2");
    expect(within(pending).getByRole("link", { name: "Открыть похожий товар: Ser Gouda 250g" })).toHaveAttribute("href", "/products/g1");
  });

  it("falls back to number and code when the currency code is not one Intl accepts", async () => {
    vi.mocked(getReviewQueue).mockResolvedValue({
      totalCount: 1,
      groups: [{ kind: "totalMismatch", totalCount: 1, items: [item({ kind: "totalMismatch", id: "totalMismatch:p4", title: "Kiosk", purchaseId: "p4", currency: "zł", receiptTotal: 20, linesTotal: 15 })] }]
    });
    renderPage();
    const card = await screen.findByRole("article", { name: "Kiosk" });
    expect(card).toHaveTextContent("Строки дают 15,00 zł, а итог чека — 20,00 zł.");
  });

  it("opens a receipt with the way back to the queue and its filters", async () => {
    vi.mocked(listTags).mockResolvedValue(householdTags);
    vi.mocked(getReviewQueue).mockResolvedValue({
      totalCount: 1,
      groups: [{ kind: "unsavedReceipt", totalCount: 1, items: [item({ kind: "unsavedReceipt", id: "unsavedReceipt:r7", title: "Lidl", receiptId: "r7" })] }]
    });
    renderPage("/needs-attention?tag=kid");

    const card = await screen.findByRole("article", { name: "Lidl" });
    fireEvent.click(within(card).getByRole("link", { name: "Открыть чек" }));
    expect(screen.getByTestId("pathname")).toHaveTextContent("/receipts/r7");
    expect(screen.getByTestId("state")).toHaveTextContent(JSON.stringify({ returnTo: "/needs-attention?tag=kid" }));
  });

  it("opens the purchase editor with the way back to the queue and its filters", async () => {
    vi.mocked(listTags).mockResolvedValue(householdTags);
    vi.mocked(getReviewQueue).mockResolvedValue({
      totalCount: 1,
      groups: [{ kind: "totalMismatch", totalCount: 1, items: [item({ kind: "totalMismatch", id: "totalMismatch:p1", title: "Żabka", purchaseId: "p1", receiptTotal: 20, linesTotal: 15 })] }]
    });
    renderPage("/needs-attention?tag=kid");

    const card = await screen.findByRole("article", { name: "Żabka" });
    fireEvent.click(within(card).getByRole("link", { name: "Исправить покупку «Żabka»" }));
    expect(screen.getByTestId("pathname")).toHaveTextContent("/purchases/p1");
    expect(screen.getByTestId("state")).toHaveTextContent(JSON.stringify({ returnTo: "/needs-attention?tag=kid" }));
  });

  describe("suggested products", () => {
    beforeEach(() => {
      vi.mocked(listCategories).mockResolvedValue(sampleCategories);
    });

    it("shows the candidates inline and keeps the picker for another product", async () => {
      vi.mocked(getReviewQueue).mockResolvedValue(pendingQueue);
      renderPage();

      const card = await screen.findByRole("article", { name: "Ser Gouda 250g" });
      // «Похоже на» and the buttons say it; no extra sentence on the card.
      expect(card).not.toHaveTextContent("Это один из товаров ниже?");
      const list = within(card).getByRole("list", { name: "Похожие товары для «Ser Gouda 250g»" });
      const options = within(list).getAllByRole("listitem");
      expect(options).toHaveLength(2);
      expect(options[0]).toHaveTextContent("Ser Gouda 150g");
      expect(options[0]).toHaveTextContent("Hochland · Молочное и яйца / Сыр");
      expect(within(options[0]).getByRole("link", { name: "Ser Gouda 150g" })).toHaveAttribute("href", "/products/g1");
      expect(within(options[1]).getByRole("button", { name: "Да, «Ser Gouda 250g» — это «Ser Edam»" })).toHaveTextContent("Да, это он");
      expect(within(card).getByRole("button", { name: "Создать новый товар «Ser Gouda 250g»" })).toHaveTextContent("Создать новый товар");
      expect(within(card).getByRole("link", { name: "Выбрать другой товар для «Ser Gouda 250g»" })).toHaveAttribute("href", "/purchases/p3?edit=1&focus=product-1");
      expect(within(card).queryByRole("link", { name: /Открыть похожий товар/ })).not.toBeInTheDocument();
    });

    it("links the line to a candidate in one tap and the item leaves the queue", async () => {
      vi.mocked(getReviewQueue).mockResolvedValueOnce(pendingQueue).mockResolvedValue({ totalCount: 0, groups: [] });
      vi.mocked(getPurchase).mockResolvedValue(storedPurchase);
      vi.mocked(updatePurchase).mockResolvedValue(storedPurchase);
      renderPage();

      fireEvent.click(await screen.findByRole("button", { name: "Да, «Ser Gouda 250g» — это «Ser Gouda 150g»" }));

      await waitFor(() => expect(updatePurchase).toHaveBeenCalledTimes(1));
      expect(getPurchase).toHaveBeenCalledWith("p3");
      expect(updatePurchase).toHaveBeenCalledWith("p3", {
        expectedUpdatedAt: "2026-09-20T10:05:00.123Z",
        merchantName: "Biedronka",
        purchasedAt: "2026-09-20T10:00:00Z",
        currency: "PLN",
        receiptTotal: 11,
        items: [
          { name: "MLEKO", quantity: 1, unit: "szt", unitPrice: 5, lineTotal: 5, kind: "product", productLink: "existing", productId: "milk-1", boundingBox: { x: 0.1, y: 0.2, width: 0.5, height: 0.05 } },
          { name: "Ser Gouda 250g", quantity: 1, unit: "szt", unitPrice: 7, lineTotal: 7, kind: "product", productLink: "existing", productId: "g1", boundingBox: null },
          { name: "RABAT", quantity: null, unit: null, unitPrice: null, lineTotal: -1, kind: "discount", productLink: "auto", productId: null, boundingBox: null }
        ]
      });
      expect(await screen.findByRole("heading", { name: "Всё проверено" })).toBeInTheDocument();
    });

    it("creates a new product named after the line", async () => {
      vi.mocked(getReviewQueue).mockResolvedValue(pendingQueue);
      vi.mocked(getPurchase).mockResolvedValue(storedPurchase);
      vi.mocked(updatePurchase).mockResolvedValue(storedPurchase);
      renderPage();

      fireEvent.click(await screen.findByRole("button", { name: "Создать новый товар «Ser Gouda 250g»" }));

      await waitFor(() => expect(updatePurchase).toHaveBeenCalledTimes(1));
      const sent = vi.mocked(updatePurchase).mock.calls[0][1];
      expect(sent.items[1]).toMatchObject({ productLink: "new", productId: null });
      expect(sent.items[0]).toMatchObject({ productLink: "existing", productId: "milk-1" });
    });

    it("does not overwrite a line someone already decided and refreshes the queue", async () => {
      vi.mocked(getReviewQueue).mockResolvedValue(pendingQueue);
      vi.mocked(getPurchase).mockResolvedValue({
        ...storedPurchase,
        items: storedPurchase.items.map((entry) => entry.position === 1 ? { ...entry, pendingMatch: false, productId: "g2", productName: "Ser Edam" } : entry)
      });
      renderPage();

      fireEvent.click(await screen.findByRole("button", { name: "Да, «Ser Gouda 250g» — это «Ser Gouda 150g»" }));

      expect(await screen.findByRole("alert")).toHaveTextContent("Эту строку уже разобрали — очередь обновлена.");
      expect(updatePurchase).not.toHaveBeenCalled();
      await waitFor(() => expect(getReviewQueue).toHaveBeenCalledTimes(2));
    });

    it("keeps the card with an error when saving fails", async () => {
      vi.mocked(getReviewQueue).mockResolvedValue(pendingQueue);
      vi.mocked(getPurchase).mockResolvedValue(storedPurchase);
      vi.mocked(updatePurchase).mockRejectedValue(new Error("offline"));
      renderPage();

      fireEvent.click(await screen.findByRole("button", { name: "Да, «Ser Gouda 250g» — это «Ser Edam»" }));

      expect(await screen.findByRole("alert")).toHaveTextContent("Не удалось связать строку с товаром. Попробуйте ещё раз.");
      expect(screen.getByRole("article", { name: "Ser Gouda 250g" })).toBeInTheDocument();
    });
  });

  describe("category filter", () => {
    const milkQueue: ReviewQueue = { totalCount: 1, groups: [queue.groups[1]] };

    beforeEach(() => {
      vi.mocked(listCategories).mockResolvedValue(sampleCategories);
    });

    it("narrows the queue to a category, keeps it in the URL and resets", async () => {
      vi.mocked(getReviewQueue).mockImplementation(async (_limit, filter) => (filter?.category === "milk" ? milkQueue : queue));
      renderPage();

      expect(await screen.findByText("4 пункта требуют внимания.")).toBeInTheDocument();
      openFilters();
      await findRow("Категория");
      expect(screen.queryByRole("button", { name: /^Тег/ })).not.toBeInTheDocument();

      pick("Категория", "Молоко");

      expect(await screen.findByText("1 пункт из категории «Молочное и яйца / Молоко» требует внимания.")).toBeInTheDocument();
      expect(getReviewQueue).toHaveBeenLastCalledWith(20, { category: "milk", tag: undefined });
      expect(screen.getByTestId("location")).toHaveTextContent("?category=milk");

      fireEvent.click(screen.getByRole("button", { name: "Сбросить фильтр" }));
      expect(await screen.findByText("4 пункта требуют внимания.")).toBeInTheDocument();
      expect(screen.getByTestId("location")).toBeEmptyDOMElement();
      expect(row("Категория")).toHaveTextContent("Все категории");
    });

    it("combines «Без категории» with a tag and resets both", async () => {
      vi.mocked(listTags).mockResolvedValue(householdTags);
      vi.mocked(getReviewQueue).mockImplementation(async (_limit, filter) => (filter?.category ? { totalCount: 0, groups: [] } : queue));
      renderPage("/needs-attention?category=none&tag=kid");

      expect(await screen.findByRole("heading", { level: 2, name: "По этому фильтру всё проверено" })).toBeInTheDocument();
      expect(screen.getByText("Нет пунктов о товарах без категории с тегом «Для ребёнка». Остальное — в полной очереди.")).toBeInTheDocument();
      expect(getReviewQueue).toHaveBeenCalledTimes(1);
      expect(getReviewQueue).toHaveBeenCalledWith(20, { category: "none", tag: "kid" });

      const resets = screen.getAllByRole("button", { name: "Сбросить фильтр" });
      fireEvent.click(resets[resets.length - 1]);
      expect(await screen.findByText("4 пункта требуют внимания.")).toBeInTheDocument();
      expect(screen.getByTestId("location")).toBeEmptyDOMElement();
    });

    it("drops a deleted category from the link without sending it", async () => {
      vi.mocked(getReviewQueue).mockResolvedValue(queue);
      renderPage("/needs-attention?category=gone");

      expect(await screen.findByText("4 пункта требуют внимания.")).toBeInTheDocument();
      expect(getReviewQueue).toHaveBeenCalledTimes(1);
      expect(getReviewQueue).toHaveBeenCalledWith(20, { category: undefined, tag: undefined });
      openFilters();
      expect(row("Категория")).toHaveTextContent("Все категории");
    });

    it("loads the whole queue when the category tree fails", async () => {
      vi.mocked(listCategories).mockRejectedValue(new Error("offline"));
      vi.mocked(getReviewQueue).mockResolvedValue(queue);
      renderPage("/needs-attention?category=milk");

      expect(await screen.findByText("4 пункта требуют внимания.", {}, { timeout: 4_000 })).toBeInTheDocument();
      expect(getReviewQueue).not.toHaveBeenCalledWith(20, expect.objectContaining({ category: "milk" }));
      // Nothing to filter by: no tree and no tags.
      expect(screen.queryByRole("button", { name: /^Фильтры/ })).not.toBeInTheDocument();
    });
  });

  describe("tag filter", () => {
    const kidQueue: ReviewQueue = { totalCount: 1, groups: [queue.groups[1]] };

    it("is hidden while the household has no tags and the whole queue is requested", async () => {
      vi.mocked(getReviewQueue).mockResolvedValue(queue);
      renderPage();
      await screen.findByRole("region", { name: "Сумма не сходится" });
      expect(getReviewQueue).toHaveBeenCalledWith(20, { category: undefined, tag: undefined });
      expect(screen.queryByLabelText("Тег")).not.toBeInTheDocument();
    });

    it("narrows the queue to a tag, keeps it in the URL and resets to the whole queue", async () => {
      vi.mocked(listTags).mockResolvedValue(householdTags);
      vi.mocked(getReviewQueue).mockImplementation(async (_limit, filter) => (filter?.tag === "kid" ? kidQueue : queue));
      renderPage();

      expect(await screen.findByText("4 пункта требуют внимания.")).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Сбросить фильтр" })).not.toBeInTheDocument();
      openFilters();
      await findRow("Тег");

      pick("Тег", "Для ребёнка");

      expect(await screen.findByText("1 пункт с тегом «Для ребёнка» требует внимания.")).toBeInTheDocument();
      expect(getReviewQueue).toHaveBeenLastCalledWith(20, { category: undefined, tag: "kid" });
      expect(screen.getByTestId("location")).toHaveTextContent("?tag=kid");
      expect(screen.getByRole("article", { name: "MLEKO 2%" })).toBeInTheDocument();
      expect(screen.queryByRole("region", { name: "Сумма не сходится" })).not.toBeInTheDocument();

      fireEvent.click(screen.getByRole("button", { name: "Сбросить фильтр" }));
      expect(await screen.findByText("4 пункта требуют внимания.")).toBeInTheDocument();
      expect(screen.getByTestId("location")).toBeEmptyDOMElement();
      expect(row("Тег")).toHaveTextContent("Все теги");
    });

    it("shows a filtered empty state with a way back to the whole queue", async () => {
      vi.mocked(listTags).mockResolvedValue(householdTags);
      vi.mocked(getReviewQueue).mockImplementation(async (_limit, filter) => (filter?.tag ? { totalCount: 0, groups: [] } : queue));
      renderPage("/needs-attention?tag=promo");

      expect(await screen.findByRole("heading", { level: 2, name: "По этому фильтру всё проверено" })).toBeInTheDocument();
      expect(screen.getByText("Нет пунктов о товарах с тегом «Акция». Остальное — в полной очереди.")).toBeInTheDocument();
      expect(screen.queryByRole("heading", { name: "Всё проверено" })).not.toBeInTheDocument();
      expect(getReviewQueue).toHaveBeenCalledTimes(1);
      expect(getReviewQueue).toHaveBeenCalledWith(20, { category: undefined, tag: "promo" });

      const resets = screen.getAllByRole("button", { name: "Сбросить фильтр" });
      fireEvent.click(resets[resets.length - 1]);
      expect(await screen.findByText("4 пункта требуют внимания.")).toBeInTheDocument();
    });

    it("drops a deleted tag from the link without sending it", async () => {
      vi.mocked(listTags).mockResolvedValue(householdTags);
      vi.mocked(getReviewQueue).mockResolvedValue(queue);
      renderPage("/needs-attention?tag=gone");

      expect(await screen.findByText("4 пункта требуют внимания.")).toBeInTheDocument();
      expect(getReviewQueue).toHaveBeenCalledTimes(1);
      expect(getReviewQueue).toHaveBeenCalledWith(20, { category: undefined, tag: undefined });
      openFilters();
      expect(row("Тег")).toHaveTextContent("Все теги");
    });

    it("loads the whole queue when the tag list fails", async () => {
      vi.mocked(listTags).mockRejectedValue(new Error("offline"));
      vi.mocked(getReviewQueue).mockResolvedValue(queue);
      renderPage("/needs-attention?tag=kid");

      expect(await screen.findByText("4 пункта требуют внимания.", {}, { timeout: 4_000 })).toBeInTheDocument();
      expect(getReviewQueue).not.toHaveBeenCalledWith(20, expect.objectContaining({ tag: "kid" }));
      expect(screen.queryByLabelText("Тег")).not.toBeInTheDocument();
    });
  });
});
