import { render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { describe, expect, it } from "vitest";
import type { ProductCategory, ProductSummary } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { categoryGroupKey } from "../categories/CategoryTile";
import { ProductCatalog } from "./ProductCatalog";

const seedId = (number: number) => `00000000-0000-4000-8000-${String(number).padStart(12, "0")}`;
const categories: ProductCategory[] = [
  { id: seedId(28), parentId: null, name: "Сладости и снеки", systemKey: "sweets_snacks", position: 5, productCount: 0 },
  { id: seedId(29), parentId: seedId(28), name: "Шоколад и конфеты", systemKey: "chocolate", position: 0, productCount: 1 },
  // Renamed by the household: no key any more, the seed id still names the group.
  { id: seedId(1), parentId: null, name: "Молочка", systemKey: null, position: 0, productCount: 0 },
  { id: seedId(2), parentId: seedId(1), name: "Молоко", systemKey: "milk", position: 0, productCount: 1 },
  // The household's own group and subcategory.
  { id: "own", parentId: null, name: "Для дачи", systemKey: null, position: 12, productCount: 0 },
  { id: "own-seeds", parentId: "own", name: "Семена", systemKey: null, position: 0, productCount: 1 }
];

function product(id: string, name: string, extra: Partial<ProductSummary>): ProductSummary {
  return {
    id, name, normalizedName: name.toLowerCase(), purchaseCount: 1, linkedItemCount: 1, aliasCount: 1,
    createdAt: "2026-07-01T10:00:00Z", updatedAt: "2026-07-21T10:00:00Z", ...extra
  };
}

const products = [
  product("chalwa", "A_CHALWA WEDEL 250G", { categoryId: seedId(29), categorySource: "suggested", brand: "Wedel" }),
  product("chalwa50", "A_CHALWA WEDEL 50G", { categoryId: null, needsReview: true }),
  product("mleko", "MLEKO 3,2% 1L", { categoryId: seedId(2), categorySource: "confirmed", brand: "Łaciate", purchaseCount: 5 }),
  product("seeds", "NASIONA MARCHEW", { categoryId: "own-seeds", categorySource: "confirmed" }),
  // Its category was deleted meanwhile.
  product("gone", "STARY TOWAR", { categoryId: "deleted", categorySource: "confirmed" })
];

function renderCatalog() {
  render(
    <AppProviders locale="ru">
      <MemoryRouter>
        <ProductCatalog query="" onQueryChange={() => undefined} categories={categories} products={products} totalCount={4} needsReviewCount={1} />
      </MemoryRouter>
    </AppProviders>
  );
}

const rowOf = (name: RegExp) => screen.getByRole("link", { name });
const tileOf = (row: HTMLElement) => row.querySelector(".pm-category-tile");

describe("ProductCatalog rows", () => {
  it("lead with the group's tile and say only the leaf category, the brand and the purchases", async () => {
    renderCatalog();

    const chalwa = await screen.findByRole("link", { name: /A_CHALWA WEDEL 250G/ });
    expect(tileOf(chalwa)).toHaveAttribute("data-group", "sweets_snacks");
    expect(tileOf(chalwa)).toHaveAttribute("aria-hidden", "true");
    expect(within(chalwa).getByText(/Шоколад и конфеты/).closest("p")).toHaveTextContent(/^Шоколад и конфеты · Wedel · 1 покупка$/);
    expect(within(chalwa).queryByText(/Сладости и снеки/)).not.toBeInTheDocument();
    // An AI suggestion: a sparkle with a name, no coloured pill.
    expect(within(chalwa).getByRole("img", { name: "предложено" })).toBeInTheDocument();
    expect(chalwa.querySelector(".pm-chip")).toBeNull();

    const mleko = rowOf(/MLEKO/);
    expect(tileOf(mleko)).toHaveAttribute("data-group", "dairy");
    expect(within(mleko).queryByRole("img", { name: "предложено" })).not.toBeInTheDocument();
    expect(within(mleko).getByText(/^Молоко$/).closest("p")).toHaveTextContent(/^Молоко · Łaciate · 5 покупок$/);
  });

  it("show «Проверить» in the warning colour, a grey tag for no category and the neutral tag for the household's own group", async () => {
    renderCatalog();

    const flagged = await screen.findByRole("link", { name: /A_CHALWA WEDEL 50G/ });
    expect(within(flagged).getByText("Проверить")).toHaveClass("pm-product-meta-review");
    expect(within(flagged).getByText("Проверить").closest("p")).toHaveTextContent(/^Проверить · без категории · 1 покупка$/);
    expect(tileOf(flagged)).toHaveAttribute("data-group", "none");

    const seeds = rowOf(/NASIONA/);
    expect(tileOf(seeds)).toHaveAttribute("data-group", "custom");
    expect(within(seeds).getByText(/^Семена$/)).toBeInTheDocument();

    const gone = rowOf(/STARY TOWAR/);
    expect(tileOf(gone)).toHaveAttribute("data-group", "none");
    expect(within(gone).getByText("без категории")).toBeInTheDocument();
  });

  it("finds the group by key or by the seed's id", () => {
    expect(categoryGroupKey(categories, seedId(29))).toBe("sweets_snacks");
    expect(categoryGroupKey(categories, seedId(2))).toBe("dairy");
    expect(categoryGroupKey(categories, "own-seeds")).toBe("custom");
    expect(categoryGroupKey(categories, null)).toBeNull();
    expect(categoryGroupKey(categories, "gone")).toBeNull();
  });
});
