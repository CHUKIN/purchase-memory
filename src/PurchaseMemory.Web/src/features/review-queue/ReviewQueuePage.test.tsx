import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, useLocation } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { classifyProduct, getReviewQueue, listTags } from "../../api/client";
import type { ReviewQueue, ReviewQueueItem, TagSummary } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { ReviewQueuePage } from "./ReviewQueuePage";

vi.mock("../../api/client", () => ({
  getReviewQueue: vi.fn(),
  classifyProduct: vi.fn(),
  listTags: vi.fn()
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
  return <output data-testid="location">{location.search}</output>;
}

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

  describe("tag filter", () => {
    const kidQueue: ReviewQueue = { totalCount: 1, groups: [queue.groups[1]] };

    it("is hidden while the household has no tags and the whole queue is requested", async () => {
      vi.mocked(getReviewQueue).mockResolvedValue(queue);
      renderPage();
      await screen.findByRole("region", { name: "Сумма не сходится" });
      expect(getReviewQueue).toHaveBeenCalledWith(20, undefined);
      expect(screen.queryByLabelText("Тег")).not.toBeInTheDocument();
    });

    it("narrows the queue to a tag, keeps it in the URL and resets to the whole queue", async () => {
      vi.mocked(listTags).mockResolvedValue(householdTags);
      vi.mocked(getReviewQueue).mockImplementation(async (_limit, tag) => (tag === "kid" ? kidQueue : queue));
      renderPage();

      const select = await screen.findByLabelText("Тег");
      expect(await screen.findByText("4 пункта требуют внимания.")).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Сбросить фильтр" })).not.toBeInTheDocument();

      fireEvent.change(select, { target: { value: "kid" } });

      expect(await screen.findByText("1 пункт с тегом «Для ребёнка» требует внимания.")).toBeInTheDocument();
      expect(getReviewQueue).toHaveBeenLastCalledWith(20, "kid");
      expect(screen.getByTestId("location")).toHaveTextContent("?tag=kid");
      expect(screen.getByText(/Число в меню считает всю очередь/)).toBeInTheDocument();
      expect(screen.getByRole("article", { name: "MLEKO 2%" })).toBeInTheDocument();
      expect(screen.queryByRole("region", { name: "Сумма не сходится" })).not.toBeInTheDocument();

      fireEvent.click(screen.getByRole("button", { name: "Сбросить фильтр" }));
      expect(await screen.findByText("4 пункта требуют внимания.")).toBeInTheDocument();
      expect(screen.getByTestId("location")).toBeEmptyDOMElement();
      expect(screen.getByLabelText("Тег")).toHaveValue("");
    });

    it("shows a filtered empty state with a way back to the whole queue", async () => {
      vi.mocked(listTags).mockResolvedValue(householdTags);
      vi.mocked(getReviewQueue).mockImplementation(async (_limit, tag) => (tag ? { totalCount: 0, groups: [] } : queue));
      renderPage("/needs-attention?tag=promo");

      expect(await screen.findByRole("heading", { level: 2, name: "С этим тегом всё проверено" })).toBeInTheDocument();
      expect(screen.getByText("Нет пунктов о товарах с тегом «Акция». Остальное — в полной очереди.")).toBeInTheDocument();
      expect(screen.queryByRole("heading", { name: "Всё проверено" })).not.toBeInTheDocument();
      expect(getReviewQueue).toHaveBeenCalledTimes(1);
      expect(getReviewQueue).toHaveBeenCalledWith(20, "promo");

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
      expect(getReviewQueue).toHaveBeenCalledWith(20, undefined);
      expect(screen.getByLabelText("Тег")).toHaveValue("");
    });

    it("loads the whole queue when the tag list fails", async () => {
      vi.mocked(listTags).mockRejectedValue(new Error("offline"));
      vi.mocked(getReviewQueue).mockResolvedValue(queue);
      renderPage("/needs-attention?tag=kid");

      expect(await screen.findByText("4 пункта требуют внимания.", {}, { timeout: 4_000 })).toBeInTheDocument();
      expect(getReviewQueue).not.toHaveBeenCalledWith(20, "kid");
      expect(screen.queryByLabelText("Тег")).not.toBeInTheDocument();
    });
  });
});
