import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation, useNavigate } from "react-router";
import { describe, expect, it, vi } from "vitest";
import { getProduct, mergeProduct } from "../../api/client";
import type { ProductDetail } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { ProductDetailPage } from "./ProductDetailPage";

const product: ProductDetail = {
  id: "p1",
  name: "Mleko Łaciate 3,2% 1L",
  normalizedName: "mleko laciate 3 2 1 l",
  createdAt: "2026-07-01T10:00:00Z",
  updatedAt: "2026-07-01T10:00:00Z",
  purchaseCount: 0,
  linkedItemCount: 0,
  unpricedItemCount: 0,
  priceSeries: [],
  frequency: null,
  aliases: [],
  tags: [{ id: "kid", name: "для ребёнка" }]
};

vi.mock("../../api/client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/client")>()),
  getProduct: vi.fn(async () => product),
  mergeProduct: vi.fn(),
  listCategories: vi.fn(async () => []),
  listTags: vi.fn(async () => { throw new Error("offline"); }),
  listProductImpressions: vi.fn(async () => []),
  listProductRateableLines: vi.fn(async () => [])
}));

describe("ProductDetailPage", () => {
  it("says the tags could not load instead of silently hiding the tag editor", async () => {
    render(
      <AppProviders locale="ru">
        <MemoryRouter initialEntries={["/products/p1"]}>
          <Routes><Route path="/products/:productId" element={<ProductDetailPage />} /></Routes>
        </MemoryRouter>
      </AppProviders>
    );

    expect(await screen.findByRole("heading", { level: 1, name: "Mleko Łaciate 3,2% 1L" })).toBeInTheDocument();
    expect(await screen.findByText("Не удалось загрузить теги.", {}, { timeout: 4000 })).toBeInTheDocument();
    // The tags the product already has are still shown read-only.
    expect(screen.getByRole("list", { name: "Теги товара" })).toHaveTextContent("для ребёнка");
  });

  it("after a merge shows the target in place of the merged-away product, so «Назад» skips it", async () => {
    const flagged: ProductDetail = {
      ...product,
      id: "f9",
      name: "ACTIV KEFI NAT 280",
      tags: [],
      needsReview: true,
      reviewCandidates: [{ productId: "k1", name: "ACTIV KEFI BOR 280", brand: null, categoryId: null, purchaseCount: 3, score: 0.75 }]
    };
    const target: ProductDetail = { ...product, id: "k1", name: "ACTIV KEFI BOR 280", tags: [] };
    vi.mocked(getProduct).mockImplementation(async (id) => (id === "f9" ? flagged : target));
    vi.mocked(mergeProduct).mockResolvedValue({
      sourceProductId: "f9", sourceName: flagged.name, targetProductId: "k1", targetName: target.name, movedLineCount: 1, movedAliasCount: 1
    });

    function Probe() {
      const location = useLocation();
      const navigate = useNavigate();
      return (
        <>
          <output data-testid="pathname">{location.pathname}</output>
          <button type="button" onClick={() => navigate(-1)}>history back</button>
        </>
      );
    }

    render(
      <AppProviders locale="ru">
        <MemoryRouter initialEntries={["/products", "/products/f9"]} initialIndex={1}>
          <Routes><Route path="/products/:productId" element={<ProductDetailPage />} /><Route path="/products" element={<p>Каталог</p>} /></Routes>
          <Probe />
        </MemoryRouter>
      </AppProviders>
    );

    fireEvent.click(await screen.findByRole("button", { name: "Да, «ACTIV KEFI NAT 280» — это «ACTIV KEFI BOR 280»" }));
    fireEvent.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "Объединить" }));

    await waitFor(() => expect(mergeProduct).toHaveBeenCalledWith("f9", "k1"));
    expect(await screen.findByRole("heading", { level: 1, name: "ACTIV KEFI BOR 280" })).toBeInTheDocument();
    expect(screen.getByTestId("pathname")).toHaveTextContent("/products/k1");
    fireEvent.click(screen.getByRole("button", { name: "history back" }));
    expect(await screen.findByText("Каталог")).toBeInTheDocument();
  });
});
