import type { Meta, StoryObj } from "@storybook/react-vite";
import { useState } from "react";
import { MemoryRouter } from "react-router";
import { fn } from "storybook/test";
import type { ProductSummary } from "../../api/types";
import { sampleCategories } from "../categories/categoryFixtures";
import { ProductCatalog, type ProductCatalogProps } from "./ProductCatalog";

const product = (
  id: string,
  name: string,
  purchaseCount: number,
  aliasCount: number
): ProductSummary => ({
  id,
  name,
  normalizedName: name.toLowerCase(),
  purchaseCount,
  linkedItemCount: purchaseCount + 1,
  aliasCount,
  createdAt: "2026-06-02T17:30:00Z",
  updatedAt: "2026-08-09T17:30:00Z"
});

const products = [
  product("2f1c0a10-0000-4000-8000-000000000001", "Mleko Łaciate 3,2% 1L", 4, 3),
  product("2f1c0a10-0000-4000-8000-000000000002", "Pomidory malinowe", 3, 2),
  product("2f1c0a10-0000-4000-8000-000000000003", "Chleb żytni razowy", 2, 2),
  product("2f1c0a10-0000-4000-8000-000000000004", "Kawa Jacobs Kronung 250g", 1, 1)
];

const nextPageProducts = [
  product("2f1c0a10-0000-4000-8000-000000000005", "Woda niegazowana 1,5L", 6, 2),
  product("2f1c0a10-0000-4000-8000-000000000006", "Jajka wiejskie L 10szt", 2, 1)
];

/** Appends the next mocked page so "load more" is actually exercisable in the Canvas. */
function PagedProductCatalog(args: ProductCatalogProps) {
  const [shown, setShown] = useState(args.products);
  const [hasMore, setHasMore] = useState(args.hasMore ?? false);
  return (
    <ProductCatalog
      {...args}
      products={shown}
      hasMore={hasMore}
      onLoadMore={() => {
        args.onLoadMore?.();
        setShown([...shown, ...nextPageProducts]);
        setHasMore(false);
      }}
    />
  );
}

/** Keeps the search field typable so the filtered-empty state can be reached by hand. */
function SearchableProductCatalog(args: ProductCatalogProps) {
  const [query, setQuery] = useState(args.query);
  const matches = products.filter((entry) =>
    entry.name.toLowerCase().includes(query.trim().toLowerCase()));
  return (
    <ProductCatalog
      {...args}
      query={query}
      onQueryChange={(next) => {
        args.onQueryChange(next);
        setQuery(next);
      }}
      products={matches}
      totalCount={matches.length}
    />
  );
}

const phone = { viewport: { value: "phone", isRotated: false } };
const desktop = { viewport: { value: "desktop", isRotated: false } };

const meta = {
  title: "Purchase Memory/Products/Catalog",
  component: ProductCatalog,
  parameters: { layout: "fullscreen" },
  decorators: [(Story) => <MemoryRouter><Story /></MemoryRouter>],
  args: {
    query: "",
    onQueryChange: fn(),
    products,
    totalCount: products.length,
    pendingMatchCount: 0,
    onLoadMore: fn()
  }
} satisfies Meta<typeof ProductCatalog>;

export default meta;
type Story = StoryObj<typeof meta>;

export const PopulatedMobileRu: Story = { globals: phone };
export const PopulatedDesktopEn: Story = { globals: { ...desktop, locale: "en" } };
export const SearchableDesktopRu: Story = { globals: desktop, render: SearchableProductCatalog };
export const PagedDesktopRu: Story = {
  globals: desktop,
  args: { totalCount: 6, hasMore: true },
  render: PagedProductCatalog
};

/** Matching leaves uncertain lines unlinked; the catalog says so rather than looking complete. */
export const WithPendingMatchesMobileRu: Story = { globals: phone, args: { pendingMatchCount: 4 } };

export const EmptyMobileRu: Story = { globals: phone, args: { products: [], totalCount: 0 } };
export const EmptyDesktopEn: Story = { globals: { ...desktop, locale: "en" }, args: { products: [], totalCount: 0 } };
export const FilteredEmptyMobileRu: Story = {
  globals: phone,
  args: { products: [], totalCount: 0, query: "kawa rozpuszczalna" }
};
export const LoadingMobileRu: Story = { globals: phone, args: { products: [], totalCount: 0, loading: true } };
export const ErrorDesktopEn: Story = {
  globals: { ...desktop, locale: "en" },
  args: { products: [], totalCount: 0, error: "The product catalog could not be loaded." }
};

const classified: ProductSummary[] = [
  { ...products[0], categoryId: "milk", categorySource: "confirmed", brand: "Łaciate" },
  { ...products[1], categoryId: null, categorySource: null },
  { ...products[2], categoryId: "own-child", categorySource: "suggested" },
  { ...products[3], categoryId: "coffee", categorySource: "suggested", brand: "Jacobs" }
];

function FilterableProductCatalog(args: ProductCatalogProps) {
  const [category, setCategory] = useState("");
  const [brand, setBrand] = useState("");
  return (
    <ProductCatalog
      {...args}
      categoryFilter={category}
      onCategoryFilterChange={setCategory}
      brandFilter={brand}
      onBrandFilterChange={setBrand}
    />
  );
}

/** Chips in every state (confirmed, suggested, none, long name) and the category/brand filters. */
export const WithCategoriesMobileRu: Story = {
  globals: phone,
  args: { products: classified, categories: sampleCategories },
  render: FilterableProductCatalog
};
export const WithCategoriesDesktopEn: Story = {
  globals: { ...desktop, locale: "en" },
  args: { products: classified, categories: sampleCategories },
  render: FilterableProductCatalog
};
