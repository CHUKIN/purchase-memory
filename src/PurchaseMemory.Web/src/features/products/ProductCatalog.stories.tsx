import type { Meta, StoryObj } from "@storybook/react-vite";
import { useState } from "react";
import { MemoryRouter } from "react-router";
import { fn } from "storybook/test";
import type { ProductCategory, ProductSummary } from "../../api/types";
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
    needsReviewCount: 0,
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

/** Doubtful matches became flagged products: a «проверить» badge and the tappable way to all of them. */
export const WithProductsToCheckMobileRu: Story = {
  globals: phone,
  args: {
    needsReviewCount: 4,
    onReviewFilterChange: fn(),
    products: products.map((product, index) => index === 0 ? { ...product, needsReview: true } : product)
  }
};

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

/** Every category state (confirmed, suggested ✦, none, a long household name) and the category/brand filters. */
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

/* Story data only: every seeded group (fixed seed ids) with one subcategory, as in ProductCategorySeed.cs. */
const seedId = (number: number) => `00000000-0000-4000-8000-${String(number).padStart(12, "0")}`;
const groups: [number, string, string, number, string, string][] = [
  [1, "dairy", "Молочное и яйца", 4, "yogurt", "Йогурты и кефир"],
  [8, "meat_fish", "Мясо и рыба", 11, "sausages", "Колбасы и сосиски"],
  [13, "produce", "Овощи и фрукты", 15, "fruits", "Фрукты"],
  [17, "bakery", "Хлеб и выпечка", 18, "bread", "Хлеб"],
  [20, "grocery", "Бакалея", 21, "pasta", "Макароны"],
  [28, "sweets_snacks", "Сладости и снеки", 29, "chocolate", "Шоколад и конфеты"],
  [33, "drinks", "Напитки", 34, "coffee", "Кофе"],
  [40, "frozen", "Замороженное", 41, "frozen_meals", "Полуфабрикаты"],
  [44, "household_chemicals", "Бытовая химия", 46, "dishwashing", "Для посуды"],
  [48, "hygiene", "Гигиена", 50, "oral_care", "Уход за зубами"],
  [52, "home", "Для дома", 53, "kitchen", "Кухня"],
  [55, "other", "Прочее", 58, "bags", "Пакеты"]
];
const seededTree: ProductCategory[] = groups.flatMap(([groupNumber, groupKey, groupName, childNumber, childKey, childName], position) => [
  { id: seedId(groupNumber), parentId: null, name: groupName, systemKey: groupKey, position, productCount: 0 },
  { id: seedId(childNumber), parentId: seedId(groupNumber), name: childName, systemKey: childKey, position: 0, productCount: 1 }
]);
const groupProducts: ProductSummary[] = [
  { ...product("g-chalwa", "A_CHALWA WEDEL 250G", 1, 1), categoryId: seedId(29), categorySource: "suggested", brand: "Wedel" },
  { ...product("g-chalwa50", "A_CHALWA WEDEL 50G", 1, 1), categoryId: null, needsReview: true },
  { ...product("g-talerz", "A_CO TALERZ DESEROW", 1, 1), categoryId: seedId(53), categorySource: "suggested" },
  { ...product("g-kefir", "ACTIV KEFI BOR 280", 2, 1), categoryId: seedId(4), categorySource: "suggested" },
  ...groups.filter(([, key]) => !["sweets_snacks", "home", "dairy"].includes(key)).map(([, key, , childNumber, , childName], index) => ({
    ...product(`g-${key}`, `${childName.toUpperCase()} ${index + 1}`, index + 1, 1),
    categoryId: seedId(childNumber),
    categorySource: "confirmed" as const
  })),
  { ...product("g-mleko", "MLEKO 3,2% 1L", 5, 2), categoryId: seedId(4), categorySource: "confirmed", brand: "Łaciate" }
];

/** The approved mock: a tile per group, the leaf category, ✦ for a suggestion, «Проверить» and «без категории». */
export const CategoryTilesMobileRu: Story = {
  globals: phone,
  args: { products: groupProducts, totalCount: groupProducts.length, categories: seededTree, needsReviewCount: 1, onReviewFilterChange: fn() }
};
export const CategoryTilesMobileEn: Story = { ...CategoryTilesMobileRu, globals: { ...phone, locale: "en" } };
