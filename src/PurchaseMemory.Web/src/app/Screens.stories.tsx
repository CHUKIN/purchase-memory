import type { Decorator, Meta, StoryObj } from "@storybook/react-vite";
import { MemoryRouter } from "react-router";
import type {
  Impression,
  ProductCatalogPage,
  ProductDetail,
  PurchaseDetail,
  PurchaseFeedPage,
  PurchaseView,
  RateablePurchaseItem,
  ReceiptView,
  ReviewQueue,
  ReviewQueueItem,
  TagSummary
} from "../api/types";
import { sampleAnalyticsHistory } from "../features/analytics/analyticsFixtures";
import { sampleCategories } from "../features/categories/categoryFixtures";
import { AppRoutes } from "./App";

/*
 * Whole screens inside the real shell, fed by a stubbed `fetch`. They exist to
 * check layout at phone width with deliberately long, real-looking names
 * (Polish product lines, long store names, long brands), not to test data flow.
 */

const longMerchant = "Biedronka Jeronimo Martins Polska ul. Marszałkowska 104/122";
const longProduct = "Ser żółty Gouda w plastrach Mlekovita Polski Ser Złoty 150g";
const longBrand = "Spółdzielnia Mleczarska Mlekovita";

const tags: TagSummary[] = [
  { id: "tag-kids", name: "Для детей", productCount: 3 },
  { id: "tag-weekend", name: "Выходные на даче у родителей", productCount: 1 },
  { id: "tag-sale", name: "Акция", productCount: 5 }
];

const purchaseDetail: PurchaseDetail = {
  id: "purchase-long",
  sourceReceiptId: null,
  merchantName: longMerchant,
  purchasedAt: "2026-08-24T17:12:00Z",
  currency: "PLN",
  receiptTotal: 1284.32,
  createdAt: "2026-08-24T17:20:00Z",
  updatedAt: "2026-08-24T17:20:00Z",
  items: [
    {
      id: "line-1", position: 0, name: longProduct, quantity: 2, unit: "szt", unitPrice: 8.99, lineTotal: 17.98, kind: "product",
      discountedItemPosition: null, productId: "product-long", productName: longProduct, pendingMatch: false, candidates: [], boundingBox: null
    },
    {
      id: "line-2", position: 1, name: "RABAT Ser Gouda Mlekovita", quantity: null, unit: null, unitPrice: null, lineTotal: -3.6, kind: "discount",
      discountedItemPosition: 0, productId: null, productName: null, pendingMatch: false, candidates: [], boundingBox: null
    },
    {
      id: "line-3", position: 2, name: "Woda mineralna niegazowana Żywiec Zdrój 6×1,5L zgrzewka", quantity: 1, unit: "op", unitPrice: null, lineTotal: 1199.99, kind: "product",
      discountedItemPosition: null, productId: null, productName: null, pendingMatch: true,
      candidates: [{ productId: "water", name: "Woda Żywiec Zdrój niegazowana 1,5L", score: 0.82 }], boundingBox: null
    },
    {
      id: "line-4", position: 3, name: "BULKA KAJZERKA", quantity: 4, unit: "szt", unitPrice: 0.59, lineTotal: 2.36, kind: "product",
      discountedItemPosition: null, productId: null, productName: null, pendingMatch: false, candidates: [], boundingBox: null
    }
  ],
  issues: [
    { type: "receiptTotalMismatch", itemPosition: null, itemName: null, recognizedValue: "1 280.00", suggestedFix: "" },
    { type: "missingItemPrice", itemPosition: 2, itemName: "Woda mineralna niegazowana Żywiec Zdrój 6×1,5L zgrzewka", recognizedValue: "", suggestedFix: "" }
  ]
};

const feedPurchase = (id: string, merchantName: string | null, receiptTotal: number, itemNames: string[], issues = false): PurchaseView => ({
  id,
  merchantName,
  purchasedAt: "2026-08-24T17:12:00Z",
  currency: "PLN",
  receiptTotal,
  createdAt: "2026-08-24T17:20:00Z",
  updatedAt: "2026-08-24T17:20:00Z",
  items: itemNames.map((name) => ({ name, quantity: 1, unit: "szt", unitPrice: 4.5, lineTotal: 4.5 })),
  issues: issues ? [purchaseDetail.issues[0]] : []
});

const feed: PurchaseFeedPage = {
  items: [
    feedPurchase("purchase-long", longMerchant, 1284.32, [longProduct, "Woda mineralna niegazowana Żywiec Zdrój 6×1,5L zgrzewka", "BULKA KAJZERKA"], true),
    feedPurchase("purchase-2", "Lidl", 152.9, ["Mleko", "Chleb"]),
    feedPurchase("purchase-3", null, 12.4, ["Kawa"])
  ],
  page: 1,
  pageSize: 20,
  totalCount: 3,
  hasMore: false,
  truncated: false
};

const catalog: ProductCatalogPage = {
  items: [
    {
      id: "product-long", name: longProduct, normalizedName: "ser", categoryId: "cheese", categorySource: "suggested", brand: longBrand,
      tags: tags.slice(0, 2), purchaseCount: 12, linkedItemCount: 14, aliasCount: 3, createdAt: "2026-06-02T17:30:00Z", updatedAt: "2026-08-09T17:30:00Z"
    },
    {
      id: "product-2", name: "Mleko", normalizedName: "mleko", categoryId: "milk", categorySource: "confirmed", brand: null,
      tags: [], purchaseCount: 4, linkedItemCount: 4, aliasCount: 1, createdAt: "2026-06-02T17:30:00Z", updatedAt: "2026-08-09T17:30:00Z"
    }
  ],
  page: 1,
  pageSize: 20,
  totalCount: 2,
  hasMore: false,
  pendingMatchCount: 3
};

const points = [
  { purchasedAt: "2026-06-02T17:30:00Z", merchantName: longMerchant, unitPrice: 8.99, purchaseId: "purchase-long" },
  { purchasedAt: "2026-07-14T17:30:00Z", merchantName: "Lidl", unitPrice: 1249.99, purchaseId: "purchase-2" },
  { purchasedAt: "2026-08-09T17:30:00Z", merchantName: "Żabka", unitPrice: 9.49, purchaseId: "purchase-3" }
];

const productDetail: ProductDetail = {
  id: "product-long",
  name: longProduct,
  normalizedName: "ser",
  categoryId: "own-child",
  categorySource: "suggested",
  brand: longBrand,
  brandSource: "suggested",
  tags: tags.slice(0, 2),
  createdAt: "2026-06-02T17:30:00Z",
  updatedAt: "2026-08-09T17:30:00Z",
  purchaseCount: 12,
  linkedItemCount: 14,
  unpricedItemCount: 1,
  priceSeries: [{ currency: "PLN", unit: "szt", minimumPrice: 8.99, averagePrice: 422.82, latestPrice: 9.49, latestPoint: points[2], points }],
  frequency: { firstPurchasedAt: points[0].purchasedAt, lastPurchasedAt: points[2].purchasedAt, purchaseCount: 12, averageDaysBetweenPurchases: 6.25 },
  aliases: [
    { merchantKey: "biedronka", rawText: "SER GOUDA PLASTRY MLEKOVITA 150G ZŁOTY POLSKI" },
    { merchantKey: "lidl-sklep-numer-1234-warszawa", rawText: longProduct }
  ]
};

const impression: Impression = {
  id: "impression-1",
  purchaseId: "purchase-long",
  purchaseItemId: "line-1",
  productId: "product-long",
  itemName: longProduct,
  rating: "like",
  note: "Хорошо плавится, дети едят на завтрак. Брать снова, если будет акция в Biedronka.",
  photoUrl: null,
  authorUserId: "user-1",
  authorEmail: "very.long.family.member.address@example.com",
  isMine: true,
  createdAt: "2026-08-25T08:00:00Z",
  merchantName: longMerchant,
  purchasedAt: "2026-08-24T17:12:00Z",
  currency: "PLN",
  quantity: 2,
  unit: "szt",
  unitPrice: 8.99,
  lineTotal: 17.98
};

const unrated: RateablePurchaseItem[] = [{
  purchaseItemId: "line-3",
  purchaseId: "purchase-long",
  productId: null,
  productName: null,
  itemName: "Woda mineralna niegazowana Żywiec Zdrój 6×1,5L zgrzewka",
  merchantName: longMerchant,
  purchasedAt: "2026-08-24T17:12:00Z",
  currency: "PLN",
  unitPrice: 1199.99,
  lineTotal: 1199.99
}];

const recentReceipt: ReceiptView = {
  id: "receipt-1",
  status: "ready",
  uploadedAt: "2026-08-24T17:12:00Z",
  updatedAt: "2026-08-24T17:13:00Z",
  originalFileName: "IMG_20260824_171200_paragon_biedronka_marszalkowska.jpg",
  sourceImageUrl: "/receipt.svg",
  imageParts: [{ position: 0, contentType: "image/jpeg", sizeBytes: 42, sourceImageUrl: "/receipt.svg" }],
  provider: null,
  model: null,
  processingDurationMs: null,
  snapshot: null,
  warnings: [],
  issues: [],
  error: null,
  savedPurchaseId: null
};

// A ready receipt in the full editor (no structured issues): the legacy capture classes in styles.css.
const reviewReceipt: ReceiptView = {
  ...recentReceipt,
  id: "receipt-review",
  snapshot: {
    merchantName: longMerchant,
    purchasedAt: "2026-08-24T17:12:00Z",
    currency: "PLN",
    receiptTotal: 1284.32,
    items: [
      { name: longProduct, quantity: 2, unit: "szt", unitPrice: 8.99, lineTotal: 17.98, kind: "product" },
      { name: "RABAT Ser Gouda Mlekovita Polski Ser Złoty", quantity: null, unit: null, unitPrice: null, lineTotal: -3.6, kind: "discount" },
      { name: "Woda mineralna niegazowana Żywiec Zdrój 6×1,5L zgrzewka", quantity: 1, unit: "op", unitPrice: 1199.99, lineTotal: 1199.99, kind: "product" }
    ]
  },
  warnings: ["Сумма строк (1 214,37 zł) не совпадает с итогом чека (1 284,32 zł)."]
};

const queueItem = (overrides: Partial<ReviewQueueItem> & Pick<ReviewQueueItem, "kind" | "id">): ReviewQueueItem => ({
  title: "", occurredAt: "2026-08-24T17:12:00Z", receiptId: null, purchaseId: null, productId: null, categoryId: null, categoryName: null,
  currency: null, receiptTotal: null, linesTotal: null, affectedLineCount: null, itemPosition: null, candidates: [], ...overrides
});

const reviewQueue: ReviewQueue = {
  totalCount: 5,
  groups: [
    { kind: "totalMismatch", totalCount: 1, items: [queueItem({ kind: "totalMismatch", id: "totalMismatch:p1", title: longMerchant, receiptId: "receipt-1", purchaseId: "purchase-long", currency: "PLN", receiptTotal: 1284.32, linesTotal: 1214.37 })] },
    { kind: "missingPrice", totalCount: 1, items: [queueItem({ kind: "missingPrice", id: "missingPrice:p1", title: longMerchant, purchaseId: "purchase-long", affectedLineCount: 1, itemPosition: 2 })] },
    {
      kind: "pendingMatch", totalCount: 1, items: [queueItem({
        kind: "pendingMatch", id: "pendingMatch:p1", title: "Woda mineralna niegazowana Żywiec Zdrój 6×1,5L zgrzewka", purchaseId: "purchase-long", productId: "water", itemPosition: 2,
        candidates: [
          { productId: "water", name: "Woda Żywiec Zdrój niegazowana 1,5L", brand: "Żywiec Zdrój", categoryId: "milk" },
          { productId: "product-long", name: longProduct, brand: longBrand, categoryId: "own-child" }
        ]
      })]
    },
    { kind: "suggestedCategory", totalCount: 1, items: [queueItem({ kind: "suggestedCategory", id: "suggestedCategory:x", title: longProduct, productId: "product-long", categoryId: "cheese", categoryName: "Сыры и молочные продукты для завтрака" })] },
    { kind: "missingCategory", totalCount: 1, items: [queueItem({ kind: "missingCategory", id: "missingCategory:y", title: "Jogurt Danio waniliowy z kawałkami owoców 4×140g", productId: "y1" })] }
  ]
};

function route(path: string): unknown {
  if (path === "/api/receipts/receipt-review") return reviewReceipt;
  if (path.startsWith("/api/review-queue?")) return reviewQueue;
  if (path === "/api/analytics/context") return { enabled: true, today: "2026-09-27", merchants: ["Biedronka", "Lidl"], brands: [longBrand] };
  if (path.startsWith("/api/analytics/history?")) return { items: sampleAnalyticsHistory, hasMore: true };
  if (path === "/api/recognition/status") return { available: true, mode: "Hybrid", models: ["glm-ocr", "qwen3.5:4b"], message: null };
  if (path === "/api/review-queue/count") return { totalCount: 7, byKind: {} };
  if (path.startsWith("/api/receipts?")) return [recentReceipt];
  if (path.startsWith("/api/purchases/") && path.endsWith("/impressions")) return [impression];
  if (path.startsWith("/api/purchases/")) return purchaseDetail;
  if (path.startsWith("/api/purchases")) return feed;
  if (path.startsWith("/api/products/") && path.endsWith("/impressions")) return [impression];
  if (path.startsWith("/api/products/") && path.endsWith("/rateable-lines")) return unrated;
  if (path.startsWith("/api/products/")) return productDetail;
  if (path.startsWith("/api/products")) return catalog;
  if (path === "/api/categories") return sampleCategories;
  if (path === "/api/brands") return [{ brand: longBrand, productCount: 1 }, { brand: "Łaciate", productCount: 4 }];
  if (path === "/api/tags") return tags;
  if (path.startsWith("/api/impressions/unrated")) return unrated;
  return undefined;
}

const realFetch = typeof window === "undefined" ? undefined : window.fetch.bind(window);

const withStubbedApi: Decorator = (Story) => {
  window.fetch = async (input, init) => {
    const url = new URL(typeof input === "string" ? input : input instanceof URL ? input.href : input.url, window.location.origin);
    if (!url.pathname.startsWith("/api/")) return realFetch!(input, init);
    const body = route(url.pathname + url.search);
    return body === undefined
      ? new Response(null, { status: 404 })
      : new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } });
  };
  return <Story />;
};

function Screen({ path }: { path: string }) {
  return <MemoryRouter initialEntries={[path]}><AppRoutes /></MemoryRouter>;
}

const phone = { viewport: { value: "phone", isRotated: false } };
const desktop = { viewport: { value: "desktop", isRotated: false } };

const meta = {
  title: "Purchase Memory/Screens",
  component: Screen,
  parameters: { layout: "fullscreen" },
  decorators: [withStubbedApi],
  globals: phone,
  args: { path: "/" }
} satisfies Meta<typeof Screen>;

export default meta;
type Story = StoryObj<typeof meta>;

export const HomeMobile: Story = {};
export const PurchasesMobile: Story = { args: { path: "/purchases" } };
export const PurchaseDetailMobile: Story = { args: { path: "/purchases/purchase-long" } };
export const PurchaseEditMobile: Story = { args: { path: "/purchases/purchase-long?edit=1" } };
export const ProductsMobile: Story = { args: { path: "/products" } };
export const ProductDetailMobile: Story = { args: { path: "/products/product-long" } };
export const HelpMobile: Story = { args: { path: "/help" } };
export const PurchaseDetailMobileEn: Story = { args: { path: "/purchases/purchase-long" }, globals: { ...phone, locale: "en" } };
export const ProductDetailMobileEn: Story = { args: { path: "/products/product-long" }, globals: { ...phone, locale: "en" } };
export const PurchaseDetailDesktop: Story = { args: { path: "/purchases/purchase-long" }, globals: desktop };
export const ProductDetailDesktop: Story = { args: { path: "/products/product-long" }, globals: desktop };
export const CaptureMobile: Story = { args: { path: "/purchases/new" } };
export const ReceiptReviewMobile: Story = { args: { path: "/receipts/receipt-review" } };
export const ReceiptReviewMobileEn: Story = { args: { path: "/receipts/receipt-review" }, globals: { ...phone, locale: "en" } };
export const ReviewQueueMobile: Story = { args: { path: "/needs-attention" } };
export const ReviewQueueMobileEn: Story = { args: { path: "/needs-attention" }, globals: { ...phone, locale: "en" } };
export const ReviewQueueDesktop: Story = { args: { path: "/needs-attention" }, globals: desktop };
export const AnalyticsMobile: Story = { args: { path: "/analytics" } };
export const AnalyticsDesktop: Story = { args: { path: "/analytics" }, globals: desktop };
