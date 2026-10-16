import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { getProductCatalog, listTags } from "../../api/client";
import type { ProductCatalogPage, ProductSummary } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { findRow, openFilters, pick, pickerOptions, row } from "../../test/ionicHelpers";
import { ProductCatalogSection } from "./ProductCatalogSection";

vi.mock("../../api/client", () => ({
  getProductCatalog: vi.fn(),
  listBrands: vi.fn().mockResolvedValue([{ brand: "Łaciate", productCount: 2 }]),
  listCategories: vi.fn().mockResolvedValue([
    { id: "dairy", parentId: null, name: "Молочное и яйца", systemKey: "dairy", position: 0, productCount: 0 },
    { id: "milk", parentId: "dairy", name: "Молоко", systemKey: "milk", position: 0, productCount: 1 }
  ]),
  listTags: vi.fn().mockResolvedValue([{ id: "vegan", name: "веган", productCount: 1 }])
}));

function product(id: string, name: string): ProductSummary {
  return {
    id,
    name,
    normalizedName: name.toLowerCase(),
    purchaseCount: 2,
    linkedItemCount: 3,
    aliasCount: 2,
    createdAt: "2026-07-01T10:00:00Z",
    updatedAt: "2026-07-21T10:00:00Z"
  };
}

function page(items: ProductSummary[], overrides: Partial<ProductCatalogPage> = {}): ProductCatalogPage {
  return {
    items,
    page: 1,
    pageSize: 30,
    totalCount: items.length,
    hasMore: false,
    needsReviewCount: 0,
    ...overrides
  };
}

function renderSection(initialEntry = "/products") {
  render(
    <AppProviders locale="ru">
      <MemoryRouter initialEntries={[initialEntry]}>
        <ProductCatalogSection />
      </MemoryRouter>
    </AppProviders>
  );
}

describe("ProductCatalogSection", () => {
  beforeEach(() => {
    vi.mocked(getProductCatalog).mockReset();
    vi.mocked(getProductCatalog).mockResolvedValue(page([product("a", "Mleko Łaciate 3,2% 1L")]));
  });

  it("requests the first page with no search on mount", async () => {
    renderSection();

    await screen.findByRole("heading", { name: "Mleko Łaciate 3,2% 1L" });
    expect(getProductCatalog).toHaveBeenCalledWith({ query: "", category: "", brand: "", tag: "", page: 1, pageSize: 30 });
  });

  // Two jars of yoghurt on one receipt are two lines and one shopping trip. The
  // catalog must count trips, because the product page's frequency block does.
  it("counts shopping trips rather than receipt lines", async () => {
    renderSection();

    expect(await screen.findByText(/2 покупки/)).toBeInTheDocument();
    expect(screen.queryByText(/3 покупки/)).not.toBeInTheDocument();
  });

  it("links each product to its own page", async () => {
    renderSection();

    const link = await screen.findByRole("link", { name: /Mleko Łaciate/ });
    expect(link).toHaveAttribute("href", "/products/a");
  });

  it("debounces the search into a single request", async () => {
    renderSection();
    await screen.findByRole("heading", { name: "Mleko Łaciate 3,2% 1L" });

    fireEvent.change(screen.getByLabelText("Поиск товара"), { target: { value: "laciate" } });

    await waitFor(() => expect(getProductCatalog).toHaveBeenCalledWith(
      expect.objectContaining({ query: "laciate", page: 1 })
    ));
  });

  it("filters by category and by brand", async () => {
    renderSection();
    await screen.findByRole("heading", { name: "Mleko Łaciate 3,2% 1L" });

    openFilters();
    await findRow("Категория");
    pick("Категория", "Вся группа");
    await waitFor(() => expect(getProductCatalog).toHaveBeenCalledWith(
      expect.objectContaining({ category: "dairy", page: 1 })
    ));

    pick("Бренд", "Łaciate");
    await waitFor(() => expect(getProductCatalog).toHaveBeenCalledWith(
      expect.objectContaining({ category: "dairy", brand: "Łaciate" })
    ));
  });

  it("appends the next page when the reader asks for more", async () => {
    vi.mocked(getProductCatalog)
      .mockResolvedValueOnce(page([product("a", "Mleko")], { totalCount: 2, hasMore: true }))
      .mockResolvedValueOnce(page([product("b", "Chleb")], { page: 2, totalCount: 2 }));

    renderSection();
    await screen.findByRole("heading", { name: "Mleko" });

    fireEvent.click(screen.getByRole("button", { name: "Показать ещё" }));

    expect(await screen.findByRole("heading", { name: "Chleb" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Mleko" })).toBeInTheDocument();
  });

  it("offers a way out when a search matches nothing", async () => {
    vi.mocked(getProductCatalog).mockResolvedValue(page([]));

    renderSection();
    await screen.findByRole("heading", { name: "Каталог пока пуст" });

    fireEvent.change(screen.getByLabelText("Поиск товара"), { target: { value: "zzz" } });

    expect(await screen.findByRole("heading", { name: "Ничего не нашлось" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Сбросить поиск" }));
    expect(await screen.findByRole("heading", { name: "Каталог пока пуст" })).toBeInTheDocument();
  });

  it("marks flagged products and turns «N товаров нужно проверить» into the filter", async () => {
    vi.mocked(getProductCatalog).mockImplementation(async (request) => request?.needsReview
      ? page([{ ...product("f", "ACTIV KEFI NAT 280"), needsReview: true }], { needsReviewCount: 4 })
      : page([product("a", "Mleko"), { ...product("f", "ACTIV KEFI NAT 280"), needsReview: true }], { needsReviewCount: 4 }));

    renderSection();

    const flagged = await screen.findByRole("link", { name: /ACTIV KEFI NAT 280/ });
    expect(within(flagged).getByText("проверить")).toBeInTheDocument();
    expect(within(screen.getByRole("link", { name: /Mleko/ })).queryByText("проверить")).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "4 товара нужно проверить" }));

    await waitFor(() => expect(getProductCatalog).toHaveBeenLastCalledWith(expect.objectContaining({ needsReview: true })));
    expect(await screen.findByText("1 товар")).toBeInTheDocument();
    // The hint is the way in; with the filter on it steps aside, and the sheet shows the filter.
    expect(screen.queryByRole("button", { name: "4 товара нужно проверить" })).not.toBeInTheDocument();
    openFilters();
    expect(screen.getByRole("switch", { name: "Нужно проверить" })).toBeChecked();
  });

  it("hides the hint when nothing needs checking", async () => {
    vi.mocked(getProductCatalog).mockResolvedValue(page([product("a", "Mleko")], { needsReviewCount: 0 }));

    renderSection();

    await screen.findByRole("link", { name: /Mleko/ });
    expect(screen.queryByRole("button", { name: /нужно проверить/ })).not.toBeInTheDocument();
  });

  it("reads the «Нужно проверить» filter from the URL", async () => {
    renderSection("/products?review=1");

    await waitFor(() => expect(getProductCatalog).toHaveBeenCalledWith(expect.objectContaining({ needsReview: true })));
  });

  it("shows the load error copy when the request fails", async () => {
    vi.mocked(getProductCatalog).mockRejectedValue(new Error("offline"));

    renderSection();

    expect(await screen.findByRole("alert", {}, { timeout: 4000 }))
      .toHaveTextContent("Не удалось загрузить каталог товаров.");
  });

  it("reads its filters from the URL and offers the known brands", async () => {
    render(
      <AppProviders locale="ru">
        <MemoryRouter initialEntries={["/products?category=none&brand=laciate&query=mleko"]}>
          <ProductCatalogSection />
        </MemoryRouter>
      </AppProviders>
    );

    await screen.findByRole("heading", { name: "Mleko Łaciate 3,2% 1L" });
    expect(getProductCatalog).toHaveBeenCalledWith({ query: "mleko", category: "none", brand: "laciate", tag: "", page: 1, pageSize: 30 });
    expect(screen.getByLabelText("Поиск товара")).toHaveValue("mleko");
    expect(screen.getByRole("button", { name: "Фильтры, выбрано 2" })).toBeInTheDocument();
    openFilters();
    expect(row("Бренд")).toHaveTextContent("laciate");
    await waitFor(() => expect(pickerOptions("Бренд")).toEqual(["Любой бренд", "Łaciate 2 товара"]));
  });

  it("shows each product's tags and filters by a tag from the URL", async () => {
    vi.mocked(getProductCatalog).mockResolvedValue(page([
      { ...product("a", "Mleko Łaciate 3,2% 1L"), tags: [{ id: "vegan", name: "веган" }] }
    ]));
    renderSection("/products?tag=vegan");

    await screen.findByRole("heading", { name: "Mleko Łaciate 3,2% 1L" });
    expect(getProductCatalog).toHaveBeenCalledWith(expect.objectContaining({ tag: "vegan" }));
    expect(screen.getByRole("list", { name: "Теги товара" })).toHaveTextContent("веган");
    openFilters();
    expect(await findRow("Тег")).toHaveTextContent("веган");
  });

  it("resets a stale tag and clears the tag with «Сбросить поиск»", async () => {
    renderSection("/products?tag=gone");
    await screen.findByRole("heading", { name: "Mleko Łaciate 3,2% 1L" });
    expect(getProductCatalog).not.toHaveBeenCalledWith(expect.objectContaining({ tag: "gone" }));
    const sheet = openFilters();
    expect(await findRow("Тег")).toHaveTextContent("Все теги");

    vi.mocked(getProductCatalog).mockResolvedValue(page([]));
    pick("Тег", "веган");
    await waitFor(() => expect(getProductCatalog).toHaveBeenCalledWith(expect.objectContaining({ tag: "vegan" })));
    fireEvent.click(within(sheet).getByRole("button", { name: "Готово" }));
    fireEvent.click(await screen.findByRole("button", { name: "Сбросить поиск" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Фильтры" })).toBeInTheDocument());
    expect(listTags).toHaveBeenCalled();
  });

  it("drops the tag from a link when the tag list cannot load", async () => {
    vi.mocked(listTags).mockRejectedValue(new Error("offline"));
    renderSection("/products?tag=maybe-stale");
    expect(await screen.findByRole("heading", { name: "Mleko Łaciate 3,2% 1L" }, { timeout: 8000 })).toBeInTheDocument();
    expect(getProductCatalog).not.toHaveBeenCalledWith(expect.objectContaining({ tag: "maybe-stale" }));
    expect(getProductCatalog).toHaveBeenCalledWith(expect.objectContaining({ tag: "" }));
    vi.mocked(listTags).mockResolvedValue([{ id: "vegan", name: "веган", productCount: 1 }]);
  }, 10000);
});
