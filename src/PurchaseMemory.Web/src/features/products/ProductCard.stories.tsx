import type { Meta, StoryObj } from "@storybook/react-vite";
import { MemoryRouter } from "react-router";
import type { ProductDetail, ProductPricePoint, ProductPriceSeries } from "../../api/types";
import { fn } from "storybook/test";
import { sampleCategories } from "../categories/categoryFixtures";
import { ProductCard } from "./ProductCard";

const point = (purchasedAt: string, merchantName: string | null, unitPrice: number): ProductPricePoint => ({
  purchasedAt,
  merchantName,
  unitPrice,
  purchaseId: `${purchasedAt}-${unitPrice}`
});

function series(unit: string, points: ProductPricePoint[], currency: string | null = "PLN"): ProductPriceSeries {
  const prices = points.map((entry) => entry.unitPrice);
  return {
    currency,
    unit,
    minimumPrice: Math.min(...prices),
    averagePrice: Number((prices.reduce((sum, price) => sum + price, 0) / prices.length).toFixed(2)),
    latestPrice: prices[prices.length - 1],
    latestPoint: points[points.length - 1],
    points
  };
}

const milkPrices = series("szt", [
  point("2026-06-02T17:30:00Z", "Biedronka", 4.79),
  point("2026-06-21T17:30:00Z", "Lidl", 4.29),
  point("2026-07-14T17:30:00Z", "Żabka", 5.99),
  point("2026-08-09T17:30:00Z", "Biedronka", 5.29)
]);

const milk: ProductDetail = {
  id: "2f1c0a10-0000-4000-8000-000000000001",
  name: "Mleko Łaciate 3,2% 1L",
  normalizedName: "mleko laciate 3 2 1 l",
  createdAt: "2026-06-02T17:30:00Z",
  updatedAt: "2026-08-09T17:30:00Z",
  purchaseCount: 4,
  linkedItemCount: 4,
  unpricedItemCount: 0,
  priceSeries: [milkPrices],
  frequency: {
    firstPurchasedAt: "2026-06-02T17:30:00Z",
    lastPurchasedAt: "2026-08-09T17:30:00Z",
    purchaseCount: 4,
    averageDaysBetweenPurchases: 22.67
  },
  aliases: [
    { merchantKey: "biedronka", rawText: "Mleko Łaciate 3,2% 1L" },
    { merchantKey: "lidl", rawText: "MLEKO LACIATE 3,2% 1L B" },
    { merchantKey: "zabka", rawText: "Mleko Laciate 3,2% 1L" }
  ]
};

/**
 * Sold by weight in the big chains and by the piece in the corner shop. The two
 * prices must stay in separate series: averaging «11.49 per kg» with «2.49 per
 * piece» would produce a number that describes nothing.
 */
const tomatoes: ProductDetail = {
  ...milk,
  id: "2f1c0a10-0000-4000-8000-000000000002",
  name: "Pomidory malinowe",
  normalizedName: "pomidory malinowe",
  purchaseCount: 4,
  linkedItemCount: 4,
  priceSeries: [
    series("kg", [
      point("2026-06-02T17:30:00Z", "Biedronka", 12.99),
      point("2026-06-21T17:30:00Z", "Lidl", 10.49),
      point("2026-08-09T17:30:00Z", "Biedronka", 11.49)
    ]),
    series("szt", [point("2026-07-14T17:30:00Z", "Żabka", 2.49)])
  ],
  aliases: [
    { merchantKey: "biedronka", rawText: "Pomidory malinowe" },
    { merchantKey: "lidl", rawText: "POMIDORY MALINOWE" },
    { merchantKey: "zabka", rawText: "Pomidory malinowe szt." }
  ]
};

/** Bought once: a price, but deliberately no rhythm and nothing for a line to draw. */
const boughtOnce: ProductDetail = {
  ...milk,
  id: "2f1c0a10-0000-4000-8000-000000000003",
  name: "Kawa Jacobs Kronung 250g",
  normalizedName: "kawa jacobs kronung 250 g",
  purchaseCount: 1,
  linkedItemCount: 1,
  priceSeries: [series("szt", [point("2026-06-02T17:30:00Z", "Biedronka", 24.99)])],
  frequency: {
    firstPurchasedAt: "2026-06-02T17:30:00Z",
    lastPurchasedAt: "2026-06-02T17:30:00Z",
    purchaseCount: 1,
    averageDaysBetweenPurchases: null
  },
  aliases: [{ merchantKey: "biedronka", rawText: "Kawa Jacobs Kronung 250g" }]
};

/** Every line's price was unreadable, so the page says so instead of drawing nothing. */
const withoutPrices: ProductDetail = {
  ...milk,
  id: "2f1c0a10-0000-4000-8000-000000000004",
  name: "Woda niegazowana 1,5L",
  normalizedName: "woda niegazowana 1 5 l",
  purchaseCount: 2,
  linkedItemCount: 2,
  unpricedItemCount: 2,
  priceSeries: [],
  frequency: null,
  aliases: [{ merchantKey: "zabka", rawText: "Woda niegazowana 1,5L" }]
};

/** Some lines priced, some not: the numbers stand, with the gap admitted. */
const partiallyPriced: ProductDetail = {
  ...milk,
  id: "2f1c0a10-0000-4000-8000-000000000005",
  unpricedItemCount: 2
};

/** A receipt that never stated a currency; the household default stands in. */
const withoutCurrency: ProductDetail = {
  ...milk,
  id: "2f1c0a10-0000-4000-8000-000000000006",
  priceSeries: [series("szt", milkPrices.points, null)]
};

const phone = { viewport: { value: "phone", isRotated: false } };
const desktop = { viewport: { value: "desktop", isRotated: false } };

const meta = {
  title: "Purchase Memory/Products/Card",
  component: ProductCard,
  parameters: { layout: "padded" },
  decorators: [(Story) => <MemoryRouter><Story /></MemoryRouter>],
  args: { product: milk, locale: "ru-RU" }
} satisfies Meta<typeof ProductCard>;

export default meta;
type Story = StoryObj<typeof meta>;

export const PopulatedMobileRu: Story = { globals: phone };
export const PopulatedDesktopEn: Story = {
  globals: { ...desktop, locale: "en" },
  args: { locale: "en-GB" }
};
export const SeveralUnitsDesktopRu: Story = { globals: desktop, args: { product: tomatoes } };
export const SeveralUnitsMobileEn: Story = {
  globals: { ...phone, locale: "en" },
  args: { product: tomatoes, locale: "en-GB" }
};
export const BoughtOnceMobileRu: Story = { globals: phone, args: { product: boughtOnce } };
export const PartiallyPricedDesktopRu: Story = { globals: desktop, args: { product: partiallyPriced } };
export const WithoutPricesMobileRu: Story = { globals: phone, args: { product: withoutPrices } };
export const WithoutCurrencyDesktopEn: Story = {
  globals: { ...desktop, locale: "en" },
  args: { product: withoutCurrency, locale: "en-GB" }
};

export const SuggestedCategoryMobileRu: Story = {
  globals: phone,
  args: {
    product: { ...milk, categoryId: "milk", categorySource: "suggested", brand: "Łaciate", brandSource: "suggested" },
    categories: sampleCategories,
    onClassify: fn(async () => undefined)
  }
};
export const ConfirmedCategoryDesktopRu: Story = {
  globals: desktop,
  args: {
    product: { ...milk, categoryId: "milk", categorySource: "confirmed", brand: "Łaciate", brandSource: "confirmed" },
    categories: sampleCategories,
    onClassify: fn(async () => undefined)
  }
};
export const UncategorizedDesktopEn: Story = {
  globals: { ...desktop, locale: "en" },
  args: { product: { ...milk, categoryId: null, categorySource: null }, categories: sampleCategories, onClassify: fn(async () => undefined) }
};

const tags = [
  { id: "kid", name: "для ребёнка", productCount: 4 },
  { id: "promo", name: "акция", productCount: 9 },
  { id: "vegan", name: "веган", productCount: 1 }
];
export const WithTagsMobileRu: Story = {
  globals: phone,
  args: {
    product: { ...milk, categoryId: "milk", categorySource: "confirmed", tags: [tags[1], tags[0]] },
    categories: sampleCategories,
    onClassify: fn(async () => undefined),
    allTags: tags,
    onSetTags: fn(async () => undefined),
    onCreateTag: fn(async (name: string) => ({ id: `new-${name}`, name }))
  }
};
export const ReadOnlyTagsDesktopEn: Story = {
  globals: { ...desktop, locale: "en" },
  args: { product: { ...milk, tags: [tags[0]] }, locale: "en-GB" }
};
