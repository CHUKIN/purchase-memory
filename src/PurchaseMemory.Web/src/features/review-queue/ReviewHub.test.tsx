import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { classifyProduct, confirmDistinctProduct, createImpression, getReviewQueue, listCategories, listTags } from "../../api/client";
import type { ReviewQueue, ReviewQueueItem } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { useBackTarget } from "../../app/backNavigation";
import { clearPlaces } from "../../app/place";
import { useBack } from "../../design-system/components";
import { useRememberedBarcodes } from "../product-scan/useProductMedia";
import { ReviewQueuePage } from "./ReviewQueuePage";

vi.mock("../../api/client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/client")>()),
  getReviewQueue: vi.fn(),
  classifyProduct: vi.fn(),
  createImpression: vi.fn(),
  listTags: vi.fn(),
  listCategories: vi.fn(),
  mergeProduct: vi.fn(),
  confirmDistinctProduct: vi.fn()
}));

vi.mock("../product-scan/useProductMedia", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../product-scan/useProductMedia")>()),
  useRememberedBarcodes: vi.fn(),
  useLinkBarcode: () => vi.fn(),
  useForgetBarcode: () => vi.fn()
}));

function item(overrides: Partial<ReviewQueueItem> & Pick<ReviewQueueItem, "kind" | "id">): ReviewQueueItem {
  return {
    title: "",
    occurredAt: "2026-10-20T10:00:00Z",
    receiptId: null,
    purchaseId: null,
    productId: null,
    categoryId: null,
    categoryName: null,
    currency: "PLN",
    receiptTotal: null,
    linesTotal: null,
    affectedLineCount: null,
    ...overrides
  };
}

const suggested = (id: string, title: string) =>
  item({ kind: "suggestedCategory", id: `suggestedCategory:${id}`, title, productId: id, categoryId: "milk", categoryName: "Молоко" });

const hub: ReviewQueue = {
  totalCount: 9,
  groups: [
    { kind: "failedReceipt", totalCount: 1, items: [item({ kind: "failedReceipt", id: "failedReceipt:r0", receiptId: "r0" })] },
    {
      kind: "productNeedsReview",
      totalCount: 1,
      items: [item({ kind: "productNeedsReview", id: "productNeedsReview:f1", title: "Ser Gouda 250g", productId: "f1", candidates: [] })]
    },
    {
      kind: "suggestedCategory",
      totalCount: 4,
      items: [suggested("m1", "MLEKO 2%"), suggested("m2", "MLEKO 3,2%"), suggested("m3", "KEFIR"), suggested("m4", "MASLANKA")]
    },
    {
      kind: "unapprovedLines",
      totalCount: 1,
      items: [item({ kind: "unapprovedLines", id: "unapprovedLines:p9", title: "Lidl", purchaseId: "p9", affectedLineCount: 3, lineCount: 5, itemPosition: 2 })]
    },
    {
      kind: "unratedPurchase",
      totalCount: 2,
      items: [
        item({ kind: "unratedPurchase", id: "unratedPurchase:l1", title: "JAJA", purchaseId: "p9", productId: "e1", purchaseItemId: "l1", merchantName: "Lidl", price: 9.99 }),
        item({ kind: "unratedPurchase", id: "unratedPurchase:l2", title: "SER", purchaseId: "p9", productId: "s1", purchaseItemId: "l2", merchantName: "Lidl", price: 6.99 })
      ]
    }
  ],
  summary: {
    totalCount: 9,
    sections: [
      { section: "receipts", count: 1 },
      { section: "purchases", count: 1 },
      { section: "products", count: 1 },
      { section: "categories", count: 4 },
      { section: "ratings", count: 2 }
    ]
  }
};

function LocationProbe() {
  const location = useLocation();
  return (
    <>
      <output data-testid="pathname">{location.pathname}</output>
      <output data-testid="search">{location.search}</output>
      <output data-testid="state">{JSON.stringify(location.state ?? null)}</output>
    </>
  );
}

/** A product screen with nothing but its back chevron's behaviour. */
function ProductStub() {
  const back = useBack(useBackTarget({ to: "/products", labelKey: "common.products" }));
  return <button type="button" onClick={back}>Назад к ревью</button>;
}

function renderHub(path = "/needs-attention") {
  render(
    <AppProviders locale="ru">
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/needs-attention" element={<ReviewQueuePage />} />
          <Route path="/products/:id" element={<ProductStub />} />
          <Route path="*" element={null} />
        </Routes>
        <LocationProbe />
      </MemoryRouter>
    </AppProviders>
  );
}

const sectionNames = () => screen.getAllByRole("region").map((region) => region.getAttribute("aria-labelledby") && within(region).getAllByRole("heading")[0].textContent);
const progress = () => screen.getByText(/^\d+ из \d+$/).textContent;

describe("«Ревью» hub", () => {
  beforeEach(() => {
    clearPlaces();
    vi.mocked(getReviewQueue).mockReset().mockResolvedValue(hub);
    vi.mocked(classifyProduct).mockReset().mockResolvedValue({} as never);
    vi.mocked(createImpression).mockReset().mockResolvedValue({} as never);
    vi.mocked(confirmDistinctProduct).mockReset().mockResolvedValue({} as never);
    vi.mocked(listTags).mockReset().mockResolvedValue([]);
    vi.mocked(listCategories).mockReset().mockResolvedValue([]);
    vi.mocked(useRememberedBarcodes).mockReset().mockReturnValue({ data: [] } as never);
  });

  afterEach(() => vi.useRealTimers());

  it("shows only the sections with something in them, most important first, each with its count", async () => {
    vi.mocked(useRememberedBarcodes).mockReturnValue({ data: [{ code: "5901234123457", rememberedAt: Date.UTC(2026, 9, 20, 10), off: null }] } as never);
    renderHub();

    await screen.findByRole("region", { name: "Чеки" });
    expect(sectionNames()).toEqual(["Чеки", "Покупки", "Товары", "Категории", "Штрихкоды", "Оценить покупки"]);
    expect(within(screen.getByRole("region", { name: "Категории" })).getByLabelText("4 пункта")).toBeInTheDocument();
    // The server's nine and the one code on this phone.
    expect(screen.getByText("10 пунктов требуют внимания.")).toBeInTheDocument();
    const barcodes = screen.getByRole("region", { name: "Штрихкоды" });
    expect(within(barcodes).getByRole("button", { name: "Привязать штрихкод 5901234123457 к товару" })).toBeInTheDocument();
  });

  it("leaves out empty sections and the barcodes when there are none", async () => {
    vi.mocked(getReviewQueue).mockResolvedValue({ totalCount: 1, groups: [hub.groups[3]] });
    renderHub();

    await screen.findByRole("region", { name: "Покупки" });
    expect(sectionNames()).toEqual(["Покупки"]);
  });

  it("shows three items of a section until «Показать все», which stays open in the address", async () => {
    renderHub();

    const categories = await screen.findByRole("region", { name: "Категории" });
    expect(within(categories).getAllByRole("article")).toHaveLength(3);
    fireEvent.click(within(categories).getByRole("button", { name: "Показать все (4)" }));
    expect(screen.getByTestId("search")).toHaveTextContent("open=categories");
    expect(within(screen.getByRole("region", { name: "Категории" })).getAllByRole("article")).toHaveLength(4);
  });

  it("opens a purchase with unchecked lines straight into its line review, coming back to the hub", async () => {
    renderHub();

    const purchases = await screen.findByRole("region", { name: "Покупки" });
    expect(purchases).toHaveTextContent("Проверено строк: 2 из 5.");
    fireEvent.click(within(purchases).getByRole("link", { name: "Проверить строки покупки «Lidl»" }));
    expect(screen.getByTestId("pathname")).toHaveTextContent("/purchases/p9");
    const state = JSON.parse(screen.getByTestId("state").textContent ?? "null");
    expect(state).toMatchObject({ openLineReview: true, returnTo: "/needs-attention" });
  });

  it("rates a recent line in place", async () => {
    renderHub();

    const ratings = await screen.findByRole("region", { name: "Оценить покупки" });
    const jaja = within(ratings).getByRole("article", { name: "JAJA" });
    expect(jaja).toHaveTextContent(/Lidl · .* · 9,99/);
    fireEvent.click(within(jaja).getByRole("tab", { name: "Нравится" }));
    await waitFor(() => expect(createImpression).toHaveBeenCalledWith({ purchaseItemId: "l1", rating: "like" }));
  });

  describe("«Принять все предложенные»", () => {
    const acceptAll = async () => {
      const categories = await screen.findByRole("region", { name: "Категории" });
      fireEvent.click(within(categories).getByRole("button", { name: "Принять все предложенные (4)" }));
      return (await screen.findByText("Принято категорий: 4")).closest("[role=status]") as HTMLElement;
    };

    it("hides the suggested categories at once and «Вернуть» brings them back without saving anything", async () => {
      renderHub();

      const toast = await acceptAll();
      expect(screen.queryByRole("region", { name: "Категории" })).not.toBeInTheDocument();
      fireEvent.click(within(toast).getByRole("button", { name: "Вернуть" }));

      expect(await screen.findByRole("region", { name: "Категории" })).toBeInTheDocument();
      expect(classifyProduct).not.toHaveBeenCalled();
    });

    it("confirms every suggested category once the toast is gone, also those not loaded on the screen", async () => {
      const many = { ...hub, groups: hub.groups.map((group) => group.kind === "suggestedCategory" ? { ...group, items: group.items.slice(0, 2) } : group) };
      vi.mocked(getReviewQueue).mockImplementation(async (limit) => limit === 100 ? hub : many);
      renderHub();

      const toast = await acceptAll();
      expect(getReviewQueue).toHaveBeenLastCalledWith(100, { category: undefined, tag: undefined });
      fireEvent.click(within(toast).getByRole("button", { name: "Закрыть" }));

      await waitFor(() => expect(classifyProduct).toHaveBeenCalledTimes(4));
      expect(classifyProduct).toHaveBeenCalledWith("m1", { categoryId: "milk" });
      expect(classifyProduct).toHaveBeenCalledWith("m4", { categoryId: "milk" });
    });

    it("confirms at once when the page is hidden, and a late «Вернуть» says they are already accepted", async () => {
      renderHub();

      const toast = await acceptAll();
      await act(async () => { window.dispatchEvent(new Event("pagehide")); });
      await waitFor(() => expect(classifyProduct).toHaveBeenCalledTimes(4));
      fireEvent.click(within(toast).getByRole("button", { name: "Вернуть" }));

      expect(await screen.findByText(/Категории уже приняты/)).toBeInTheDocument();
      // Nothing is sent twice when the toast goes.
      expect(classifyProduct).toHaveBeenCalledTimes(4);
    });

    it("says how many it accepts when there are more than one request can carry", async () => {
      const big = { ...hub, groups: hub.groups.map((group) => group.kind === "suggestedCategory" ? { ...group, totalCount: 140 } : group) };
      vi.mocked(getReviewQueue).mockResolvedValue(big);
      renderHub();

      const categories = await screen.findByRole("region", { name: "Категории" });
      expect(within(categories).getByRole("button", { name: "Принять первые предложенные (100)" })).toBeInTheDocument();
    });
  });

  describe("«Проверять по одной»", () => {
    it("is offered where an answer fits on a card, not for receipts or purchases", async () => {
      renderHub();

      await screen.findByRole("region", { name: "Чеки" });
      for (const name of ["Товары", "Категории", "Оценить покупки"]) {
        expect(within(screen.getByRole("region", { name })).getByRole("link", { name: /Проверять по одной/ })).toBeInTheDocument();
      }
      for (const name of ["Чеки", "Покупки"]) {
        expect(within(screen.getByRole("region", { name })).queryByRole("link", { name: /Проверять по одной/ })).not.toBeInTheDocument();
      }
    });

    it("goes card by card with «N из M»: «Верно, дальше» confirms, «Пропустить» moves on, the last card returns to the hub", async () => {
      renderHub();

      const categories = await screen.findByRole("region", { name: "Категории" });
      fireEvent.click(within(categories).getByRole("link", { name: /Проверять по одной/ }));
      expect(screen.getByTestId("search")).toHaveTextContent("?cards=categories");
      await waitFor(() => expect(progress()).toBe("1 из 4"));
      expect(screen.getByRole("heading", { level: 2, name: "MLEKO 2%" })).toBeInTheDocument();

      fireEvent.click(screen.getByRole("button", { name: "Подтвердить категорию «Молоко» для «MLEKO 2%»" }));
      await waitFor(() => expect(progress()).toBe("2 из 4"));
      expect(classifyProduct).toHaveBeenCalledWith("m1", { categoryId: "milk" });

      fireEvent.click(screen.getByRole("button", { name: "Пропустить" }));
      expect(progress()).toBe("3 из 4");
      // Back to an answered card: it says so, and the button moves on.
      fireEvent.click(screen.getByRole("button", { name: "Предыдущая карточка" }));
      fireEvent.click(screen.getByRole("button", { name: "Предыдущая карточка" }));
      expect(progress()).toBe("1 из 4");
      expect(screen.getByText("Готово")).toBeInTheDocument();

      fireEvent.click(screen.getByRole("button", { name: "Дальше" }));
      fireEvent.click(screen.getByRole("button", { name: "Пропустить" }));
      fireEvent.click(screen.getByRole("button", { name: "Пропустить" }));
      expect(progress()).toBe("4 из 4");
      fireEvent.click(screen.getByRole("button", { name: "Пропустить" }));

      await waitFor(() => expect(screen.getByTestId("search")).toHaveTextContent(/^$/));
      expect(screen.getByTestId("pathname")).toHaveTextContent("/needs-attention");
      expect(await screen.findByRole("region", { name: "Категории" })).toBeInTheDocument();
    });

    it("turns cards with a swipe like the line review: left is the next card, right the previous one", async () => {
      renderHub("/needs-attention?cards=ratings");

      await waitFor(() => expect(progress()).toBe("1 из 2"));
      const swipe = (dx: number) => {
        const card = screen.getByTestId("review-card");
        fireEvent.pointerDown(card, { pointerId: 1, clientX: 200, clientY: 300 });
        fireEvent.pointerMove(card, { pointerId: 1, clientX: 200 + dx / 2, clientY: 300 });
        fireEvent.pointerUp(card, { pointerId: 1, clientX: 200 + dx, clientY: 300 });
      };
      swipe(-120);
      expect(progress()).toBe("2 из 2");
      swipe(120);
      expect(progress()).toBe("1 из 2");
    });

    it("answers a flagged product with «Это другой товар» and moves on", async () => {
      renderHub("/needs-attention?cards=products");

      await waitFor(() => expect(progress()).toBe("1 из 1"));
      fireEvent.click(screen.getByRole("button", { name: "«Ser Gouda 250g» — это другой товар" }));
      await waitFor(() => expect(confirmDistinctProduct).toHaveBeenCalledWith("f1"));
      // The last card answered: back to the hub.
      await waitFor(() => expect(screen.getByTestId("search")).toHaveTextContent(/^$/));
    });

    it("comes back from a product to the same card", async () => {
      renderHub("/needs-attention?cards=ratings");

      await waitFor(() => expect(progress()).toBe("1 из 2"));
      fireEvent.click(screen.getByRole("button", { name: "Пропустить" }));
      expect(screen.getByRole("heading", { level: 2, name: "SER" })).toBeInTheDocument();
      fireEvent.click(screen.getByRole("button", { name: "Открыть товар: SER" }));
      expect(screen.getByTestId("pathname")).toHaveTextContent("/products/s1");

      await act(async () => { fireEvent.click(screen.getByRole("button", { name: "Назад к ревью" })); });
      expect(screen.getByTestId("search")).toHaveTextContent("?cards=ratings");
      await waitFor(() => expect(progress()).toBe("2 из 2"));
      expect(screen.getByRole("heading", { level: 2, name: "SER" })).toBeInTheDocument();
    });
  });
});
