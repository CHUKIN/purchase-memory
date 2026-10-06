import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { getProductCatalog, listTags } from "../../api/client";
import type { ProductCatalogPage, ProductSummary } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
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
    pendingMatchCount: 0,
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

    fireEvent.change(await screen.findByLabelText("Категория"), { target: { value: "dairy" } });
    await waitFor(() => expect(getProductCatalog).toHaveBeenCalledWith(
      expect.objectContaining({ category: "dairy", page: 1 })
    ));

    fireEvent.change(screen.getByLabelText("Бренд"), { target: { value: "Łaciate" } });
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

  it("admits how many lines are still waiting for a manual match", async () => {
    vi.mocked(getProductCatalog).mockResolvedValue(
      page([product("a", "Mleko")], { pendingMatchCount: 4 })
    );

    renderSection();

    expect(await screen.findByText(/4 позиции ждут/)).toBeInTheDocument();
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
    const brandField = screen.getByLabelText("Бренд");
    await waitFor(() => expect(brandField).toHaveAttribute("list"));
    const options = document.getElementById(brandField.getAttribute("list")!)!.querySelectorAll("option");
    expect([...options].map((option) => option.value)).toEqual(["Łaciate"]);
  });

  it("shows each product's tags and filters by a tag from the URL", async () => {
    vi.mocked(getProductCatalog).mockResolvedValue(page([
      { ...product("a", "Mleko Łaciate 3,2% 1L"), tags: [{ id: "vegan", name: "веган" }] }
    ]));
    renderSection("/products?tag=vegan");

    await screen.findByRole("heading", { name: "Mleko Łaciate 3,2% 1L" });
    expect(getProductCatalog).toHaveBeenCalledWith(expect.objectContaining({ tag: "vegan" }));
    expect(screen.getByRole("list", { name: "Теги товара" })).toHaveTextContent("веган");
    expect(await screen.findByLabelText("Тег")).toHaveValue("vegan");
  });

  it("resets a stale tag and clears the tag with «Сбросить поиск»", async () => {
    renderSection("/products?tag=gone");
    await screen.findByRole("heading", { name: "Mleko Łaciate 3,2% 1L" });
    expect(getProductCatalog).not.toHaveBeenCalledWith(expect.objectContaining({ tag: "gone" }));
    expect(await screen.findByLabelText("Тег")).toHaveValue("");

    vi.mocked(getProductCatalog).mockResolvedValue(page([]));
    fireEvent.change(screen.getByLabelText("Тег"), { target: { value: "vegan" } });
    await waitFor(() => expect(getProductCatalog).toHaveBeenCalledWith(expect.objectContaining({ tag: "vegan" })));
    fireEvent.click(await screen.findByRole("button", { name: "Сбросить поиск" }));
    await waitFor(() => expect(screen.getByLabelText("Тег")).toHaveValue(""));
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
