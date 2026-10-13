import type { Meta, StoryObj } from "@storybook/react-vite";
import { useState } from "react";
import { MemoryRouter } from "react-router";
import { fn } from "storybook/test";
import type { PurchaseDraftIssue, PurchaseView } from "../../api/types";
import { sampleCategories } from "../categories/categoryFixtures";
import { PurchaseFeed, emptyPurchaseFeedFilters, type PurchaseFeedProps } from "./PurchaseFeed";

const purchase = (
  id: string,
  merchantName: string,
  purchasedAt: string | null,
  receiptTotal: number,
  itemCount: number,
  issues: PurchaseDraftIssue[] = []
): PurchaseView => ({
  id,
  merchantName,
  purchasedAt,
  currency: "PLN",
  receiptTotal,
  createdAt: "2026-08-24T09:00:00Z",
  updatedAt: "2026-08-24T09:00:00Z",
  items: Array.from({ length: itemCount }, (_, index) => ({
    name: `Pozycja ${index + 1}`,
    quantity: 1,
    unit: "pcs",
    unitPrice: 4.5,
    lineTotal: 4.5
  })),
  issues
});

const purchases = [
  purchase("6d0b2b2e-8b1e-4b1e-9c2b-000000000001", "Biedronka Centrum", "2026-08-24T17:12:00Z", 84.32, 12),
  purchase("6d0b2b2e-8b1e-4b1e-9c2b-000000000002", "Lidl", "2026-08-21T11:45:00Z", 152.9, 24, [
    {
      type: "receiptTotalMismatch",
      itemPosition: null,
      itemName: null,
      recognizedValue: "148.40 PLN",
      suggestedFix: "Correct the receipt total or the line totals so that they match."
    }
  ]),
  purchase("6d0b2b2e-8b1e-4b1e-9c2b-000000000003", "Żabka", "2026-08-19T08:05:00Z", 12.4, 3),
  purchase("6d0b2b2e-8b1e-4b1e-9c2b-000000000004", "Русский магазин", null, 47.15, 7)
];

const nextPagePurchases = [
  purchase("6d0b2b2e-8b1e-4b1e-9c2b-000000000005", "Carrefour", "2026-08-17T13:30:00Z", 63.2, 9),
  purchase("6d0b2b2e-8b1e-4b1e-9c2b-000000000006", "Auchan", "2026-08-15T10:05:00Z", 205.75, 31)
];

/**
 * Appends the next mocked page of purchases on "Load more" so the fade-in
 * entrance animation actually plays in the Canvas instead of only logging
 * the call to the Actions panel.
 */
function PagedPurchaseFeed(args: PurchaseFeedProps) {
  const [purchases, setPurchases] = useState(args.purchases);
  const [hasMore, setHasMore] = useState(args.hasMore ?? false);
  return (
    <PurchaseFeed
      {...args}
      purchases={purchases}
      hasMore={hasMore}
      onLoadMore={() => {
        args.onLoadMore?.();
        setPurchases([...purchases, ...nextPagePurchases]);
        setHasMore(false);
      }}
    />
  );
}

const phone = { viewport: { value: "phone", isRotated: false } };
const desktop = { viewport: { value: "desktop", isRotated: false } };
const filtered = { ...emptyPurchaseFeedFilters, query: "kawa", status: "needsAttention" as const, from: "2026-08-01", to: "2026-08-24" };

const meta = {
  title: "Purchase Memory/PurchaseFeed/Production",
  component: PurchaseFeed,
  parameters: { layout: "fullscreen" },
  decorators: [(Story) => <MemoryRouter><Story /></MemoryRouter>],
  args: {
    filters: emptyPurchaseFeedFilters,
    onFiltersChange: fn(),
    purchases,
    totalCount: purchases.length,
    onLoadMore: fn()
  }
} satisfies Meta<typeof PurchaseFeed>;

export default meta;
type Story = StoryObj<typeof meta>;

export const PopulatedMobileRu: Story = { globals: phone };
export const PopulatedDesktopEn: Story = { globals: { ...desktop, locale: "en" } };
export const PagedDesktopRu: Story = {
  globals: desktop,
  args: { totalCount: 42, hasMore: true },
  render: PagedPurchaseFeed
};
export const TruncatedDesktopEn: Story = { globals: { ...desktop, locale: "en" }, args: { totalCount: 1000, truncated: true } };
export const EmptyMobileRu: Story = { globals: phone, args: { purchases: [], totalCount: 0 } };
export const EmptyDesktopEn: Story = { globals: { ...desktop, locale: "en" }, args: { purchases: [], totalCount: 0 } };
export const FilteredEmptyMobileRu: Story = { globals: phone, args: { purchases: [], totalCount: 0, filters: filtered } };
export const FilteredEmptyDesktopEn: Story = { globals: { ...desktop, locale: "en" }, args: { purchases: [], totalCount: 0, filters: filtered } };
export const LoadingMobileRu: Story = { globals: phone, args: { purchases: [], totalCount: 0, loading: true } };
export const ErrorDesktopEn: Story = {
  globals: { ...desktop, locale: "en" },
  args: { purchases: [], totalCount: 0, error: "Could not load purchases." }
};

const dairyGroup = sampleCategories.find((category) => category.parentId === null)!;
const matchedPurchases: PurchaseView[] = purchases.slice(0, 2).map((entry, index) => ({
  ...entry,
  items: entry.items.map((item, position) => ({
    ...item,
    name: position === 0 ? (index === 0 ? "Mleko Łaciate 3,2% 1L" : "Ser Gouda plastry bardzo długa nazwa produktu 150 g") : item.name,
    matchesFilter: position === 0
  }))
}));
const brands = [{ brand: "Łaciate", productCount: 3 }, { brand: "Hochland", productCount: 1 }];

/** Category and brand filter on: the card names the lines that made it match. */
export const ByCategoryMobileRu: Story = {
  globals: phone,
  args: {
    purchases: matchedPurchases,
    totalCount: 2,
    categories: sampleCategories,
    brands,
    filters: { ...emptyPurchaseFeedFilters, category: dairyGroup.id, brand: "Łaciate" }
  }
};
export const ByCategoryDesktopEn: Story = { ...ByCategoryMobileRu, globals: { ...desktop, locale: "en" } };
export const ByCategoryEmptyDesktopRu: Story = {
  globals: desktop,
  args: {
    purchases: [],
    totalCount: 0,
    categories: sampleCategories,
    brands,
    filters: { ...emptyPurchaseFeedFilters, category: "none" }
  }
};
