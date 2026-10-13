import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { describe, expect, it, vi } from "vitest";
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
});
