import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation, useNavigate } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { getPurchaseFeed, listTags } from "../../api/client";
import type { PurchaseFeedPage, PurchaseView } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { dialog, findRow, openFilters, pick, pickerOptions, row, setDate } from "../../test/ionicHelpers";
import { PurchaseFeedSection } from "./PurchaseFeedSection";

vi.mock("../../api/client", () => ({
  getPurchaseFeed: vi.fn(),
  listCategories: vi.fn().mockResolvedValue([
    { id: "dairy", parentId: null, name: "Молочное и яйца", systemKey: "dairy", position: 0, productCount: 0 },
    { id: "milk", parentId: "dairy", name: "Молоко", systemKey: "milk", position: 0, productCount: 1 }
  ]),
  listBrands: vi.fn().mockResolvedValue([{ brand: "Łaciate", productCount: 2 }]),
  listTags: vi.fn().mockResolvedValue([
    { id: "kid", name: "для ребёнка", productCount: 3 },
    { id: "party", name: "к празднику", productCount: 1 }
  ])
}));

function purchase(id: string, merchantName: string): PurchaseView {
  return {
    id,
    merchantName,
    purchasedAt: "2026-08-24T17:12:00Z",
    currency: "PLN",
    receiptTotal: 10,
    createdAt: "2026-08-24T18:00:00Z",
    updatedAt: "2026-08-24T18:00:00Z",
    items: [{ name: "Mleko", quantity: 1, unit: "pcs", unitPrice: 10, lineTotal: 10 }],
    issues: []
  };
}

function page(items: PurchaseView[], overrides: Partial<PurchaseFeedPage> = {}): PurchaseFeedPage {
  return { items, page: 1, pageSize: 20, totalCount: items.length, hasMore: false, truncated: false, ...overrides };
}

function LocationProbe() {
  const location = useLocation();
  return <output data-testid="location">{location.pathname + location.search}</output>;
}

function renderSection(initialEntry = "/purchases") {
  render(
    <AppProviders locale="ru">
      <MemoryRouter initialEntries={[initialEntry]}>
        <Routes>
          <Route path="/purchases" element={<><PurchaseFeedSection /><LocationProbe /></>} />
        </Routes>
      </MemoryRouter>
    </AppProviders>
  );
}

describe("PurchaseFeedSection", () => {
  beforeEach(() => {
    vi.mocked(getPurchaseFeed).mockReset();
    vi.mocked(getPurchaseFeed).mockResolvedValue(page([purchase("a", "Biedronka")]));
  });

  it("requests the first page with no filters on mount", async () => {
    renderSection();

    await screen.findByRole("heading", { name: "Biedronka" });
    expect(getPurchaseFeed).toHaveBeenCalledWith({
      query: "",
      from: undefined,
      to: undefined,
      status: "all",
      category: undefined,
      brand: undefined,
      page: 1,
      pageSize: 20
    });
  });

  it("sends the selected review status and date range as query parameters", async () => {
    renderSection();
    await screen.findByRole("heading", { name: "Biedronka" });

    const sheet = openFilters();
    fireEvent.click(within(sheet).getByRole("tab", { name: "Нужно проверить" }));
    await waitFor(() => expect(getPurchaseFeed).toHaveBeenCalledWith(
      expect.objectContaining({ status: "needsAttention", page: 1 })
    ));

    setDate("Дата с", "2026-08-01");
    setDate("Дата по", "2026-08-24");
    await waitFor(() => expect(getPurchaseFeed).toHaveBeenCalledWith(
      expect.objectContaining({ from: "2026-08-01", to: "2026-08-24", status: "needsAttention" })
    ));
  });

  it("debounces the free-text search into the same request", async () => {
    renderSection();
    await screen.findByRole("heading", { name: "Biedronka" });

    fireEvent.change(screen.getByLabelText("Поиск"), { target: { value: "mleko" } });
    await waitFor(() => expect(getPurchaseFeed).toHaveBeenCalledWith(
      expect.objectContaining({ query: "mleko", page: 1 })
    ));
  });

  it("appends the next page when the reader asks for more", async () => {
    vi.mocked(getPurchaseFeed)
      .mockResolvedValueOnce(page([purchase("a", "Biedronka")], { totalCount: 2, hasMore: true }))
      .mockResolvedValueOnce(page([purchase("b", "Lidl")], { page: 2, totalCount: 2 }));

    renderSection();
    await screen.findByRole("heading", { name: "Biedronka" });

    fireEvent.click(screen.getByRole("button", { name: "Показать ещё" }));

    expect(await screen.findByRole("heading", { name: "Lidl" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Biedronka" })).toBeInTheDocument();
    expect(getPurchaseFeed).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2 }));
  });

  it("shows the load error copy when the request fails", async () => {
    vi.mocked(getPurchaseFeed).mockRejectedValue(new Error("offline"));
    renderSection();

    expect(await screen.findByRole("alert", {}, { timeout: 4000 }))
      .toHaveTextContent("Не удалось загрузить покупки.");
  });

  it("restores every filter from a shared link and sends it at once", async () => {
    renderSection("/purchases?query=mleko&status=reviewed&category=milk&brand=%C5%81aciate&from=2026-08-01");

    await screen.findByRole("heading", { name: "Biedronka" });
    expect(getPurchaseFeed).toHaveBeenCalledWith(expect.objectContaining({
      query: "mleko", status: "reviewed", category: "milk", brand: "Łaciate", from: "2026-08-01"
    }));
    expect(screen.getByRole("button", { name: "Фильтры, выбрано 4" })).toBeInTheDocument();
    const sheet = openFilters();
    expect(await findRow("Категория")).toHaveTextContent("Молочное и яйца / Молоко");
    expect(row("Бренд")).toHaveTextContent("Łaciate");
    expect(within(sheet).getByRole("tab", { name: "Проверено" })).toHaveAttribute("aria-selected", "true");
  });

  it("writes category and brand to the URL and clears them with one button", async () => {
    renderSection();
    await screen.findByRole("heading", { name: "Biedronka" });
    const sheet = openFilters();

    await findRow("Категория");
    pick("Категория", "Вся группа");
    await waitFor(() => expect(getPurchaseFeed).toHaveBeenCalledWith(expect.objectContaining({ category: "dairy" })));
    pick("Бренд", "Łaciate");
    await waitFor(() => expect(getPurchaseFeed).toHaveBeenCalledWith(
      expect.objectContaining({ category: "dairy", brand: "Łaciate" })
    ));
    expect(screen.getByTestId("location")).toHaveTextContent("/purchases?category=dairy&brand=%C5%81aciate");

    fireEvent.click(within(sheet).getByRole("button", { name: "Сбросить" }));
    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent(/^\/purchases$/));
    expect(row("Категория")).toHaveTextContent("Все категории");
    expect(row("Бренд")).toHaveTextContent("Любой бренд");
  });

  it("filters by part of a brand typed in its picker", async () => {
    renderSection();
    await screen.findByRole("heading", { name: "Biedronka" });
    openFilters();

    fireEvent.click(row("Бренд"));
    fireEvent.change(within(dialog("Бренд")).getByLabelText("Например, Lavazza"), { target: { value: "laci" } });
    fireEvent.click(within(dialog("Бренд")).getByRole("button", { name: "Бренд содержит «laci»" }));
    await waitFor(() => expect(getPurchaseFeed).toHaveBeenCalledWith(expect.objectContaining({ brand: "laci" })));
  });

  it("offers the category tree with whole groups and «Без категории»", async () => {
    renderSection();
    openFilters();
    await findRow("Категория");
    // The section header names the group, so its rows stay short in the open list.
    expect(pickerOptions("Категория")).toEqual(["Все категории", "Без категории", "Вся группа", "Молоко"]);

    pick("Категория", "Без категории");
    await waitFor(() => expect(getPurchaseFeed).toHaveBeenCalledWith(expect.objectContaining({ category: "none" })));

    // The closed row carries the full path or «— вся группа».
    pick("Категория", "Молоко");
    expect(row("Категория")).toHaveTextContent("Молочное и яйца / Молоко");
    pick("Категория", "Вся группа");
    expect(row("Категория")).toHaveTextContent("Молочное и яйца — вся группа");
  });

  it("drops a category that no longer exists instead of failing the feed", async () => {
    renderSection("/purchases?category=deleted-id");
    await screen.findByRole("heading", { name: "Biedronka" });
    await waitFor(() => expect(getPurchaseFeed).toHaveBeenLastCalledWith(expect.objectContaining({ category: undefined })));
  });

  it("points at the lines that matched the filter", async () => {
    vi.mocked(getPurchaseFeed).mockResolvedValue(page([{
      ...purchase("a", "Biedronka"),
      items: [
        { name: "Mleko Łaciate", quantity: 1, unit: "szt", unitPrice: 4, lineTotal: 4, matchesFilter: true },
        { name: "Chleb", quantity: 1, unit: "szt", unitPrice: 5, lineTotal: 5, matchesFilter: false }
      ]
    }]));
    renderSection("/purchases?brand=laciate");

    expect(await screen.findByText("Mleko Łaciate")).toBeInTheDocument();
    expect(screen.getByText("Подходит:")).toBeInTheDocument();
    expect(screen.queryByText(/Chleb/)).not.toBeInTheDocument();
  });

  it("keeps the filters after opening a purchase and going back", async () => {
    function Harness() {
      const navigate = useNavigate();
      return <button type="button" onClick={() => void navigate(-1)}>назад</button>;
    }
    render(
      <AppProviders locale="ru">
        <MemoryRouter initialEntries={["/purchases?category=milk"]}>
          <Routes>
            <Route path="/purchases" element={<PurchaseFeedSection />} />
            <Route path="/purchases/:id" element={<Harness />} />
          </Routes>
        </MemoryRouter>
      </AppProviders>
    );

    fireEvent.click(await screen.findByRole("link", { name: /^Biedronka/ }));
    fireEvent.click(await screen.findByRole("button", { name: "назад" }));

    await screen.findByRole("heading", { name: "Biedronka" });
    expect(screen.getByRole("button", { name: "Фильтры, выбран 1" })).toBeInTheDocument();
    await waitFor(() => expect(getPurchaseFeed).toHaveBeenLastCalledWith(expect.objectContaining({ category: "milk" })));
  });

  it("filters by tag, keeps it in the URL and clears it with the other filters", async () => {
    renderSection();
    await screen.findByRole("heading", { name: "Biedronka" });
    const sheet = openFilters();

    await findRow("Тег");
    expect(pickerOptions("Тег")).toEqual(["Все теги", "для ребёнка", "к празднику"]);
    pick("Тег", "для ребёнка");
    await waitFor(() => expect(getPurchaseFeed).toHaveBeenCalledWith(expect.objectContaining({ tag: "kid" })));
    pick("Категория", "Молоко");
    await waitFor(() => expect(getPurchaseFeed).toHaveBeenCalledWith(
      expect.objectContaining({ tag: "kid", category: "milk" })
    ));
    expect(screen.getByTestId("location")).toHaveTextContent("/purchases?tag=kid&category=milk");

    fireEvent.click(within(sheet).getByRole("button", { name: "Сбросить" }));
    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent(/^\/purchases$/));
    expect(row("Тег")).toHaveTextContent("Все теги");
  });

  it("restores a tag from a shared link", async () => {
    renderSection("/purchases?tag=party");
    await screen.findByRole("heading", { name: "Biedronka" });
    expect(getPurchaseFeed).toHaveBeenCalledWith(expect.objectContaining({ tag: "party" }));
    openFilters();
    expect(await findRow("Тег")).toHaveTextContent("к празднику");
  });

  it("silently drops a tag that no longer exists, without a failing request", async () => {
    renderSection("/purchases?tag=deleted-tag");
    await screen.findByRole("heading", { name: "Biedronka" });
    expect(getPurchaseFeed).toHaveBeenCalledTimes(1);
    expect(getPurchaseFeed).toHaveBeenCalledWith(expect.objectContaining({ tag: undefined }));
    expect(getPurchaseFeed).not.toHaveBeenCalledWith(expect.objectContaining({ tag: "deleted-tag" }));
    openFilters();
    expect(await findRow("Тег")).toHaveTextContent("Все теги");
  });

  it("drops the tag from a link when the tag list cannot load, instead of showing an error", async () => {
    vi.mocked(listTags).mockRejectedValueOnce(new Error("offline")).mockRejectedValue(new Error("offline"));
    renderSection("/purchases?tag=maybe-stale");
    expect(await screen.findByRole("heading", { name: "Biedronka" }, { timeout: 8000 })).toBeInTheDocument();
    expect(getPurchaseFeed).not.toHaveBeenCalledWith(expect.objectContaining({ tag: "maybe-stale" }));
    expect(getPurchaseFeed).toHaveBeenCalledWith(expect.objectContaining({ tag: undefined }));
    vi.mocked(listTags).mockResolvedValue([
      { id: "kid", name: "для ребёнка", productCount: 3 },
      { id: "party", name: "к празднику", productCount: 1 }
    ]);
  }, 10000);
});
