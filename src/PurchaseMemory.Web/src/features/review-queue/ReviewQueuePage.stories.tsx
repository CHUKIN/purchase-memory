import type { Meta, StoryObj } from "@storybook/react-vite";
import { useState } from "react";
import { MemoryRouter } from "react-router";
import { fn } from "storybook/test";
import type { ReviewQueue, ReviewQueueItem, TagSummary } from "../../api/types";
import { sampleCategories } from "../categories/categoryFixtures";
import { ReviewCardsView } from "./ReviewCards";
import { ReviewQueueView, type ReviewQueueViewProps } from "./ReviewQueuePage";

const householdTags: TagSummary[] = [
  { id: "promo", name: "Акция", productCount: 12 },
  { id: "kid", name: "Для ребёнка", productCount: 7 },
  { id: "party", name: "К празднику", productCount: 3 }
];

function item(overrides: Partial<ReviewQueueItem> & Pick<ReviewQueueItem, "kind" | "id">): ReviewQueueItem {
  return {
    title: "",
    occurredAt: "2026-09-28T10:00:00Z",
    receiptId: null,
    purchaseId: null,
    productId: null,
    categoryId: null,
    categoryName: null,
    currency: null,
    receiptTotal: null,
    linesTotal: null,
    affectedLineCount: null,
    itemPosition: null,
    candidates: [],
    ...overrides
  };
}

const pendingGouda = item({
  kind: "productNeedsReview",
  id: "productNeedsReview:f1",
  title: "Ser Gouda plastry 250g",
  productId: "f1",
  purchaseCount: 1,
  candidates: [
    { productId: "g1", name: "Ser Gouda plastry 150g", brand: "Hochland", categoryId: "cheese", purchaseCount: 7, score: 0.8 },
    { productId: "g2", name: "Ser Gouda w plastrach Mlekovita 300g — opakowanie rodzinne", brand: "Mlekovita", categoryId: "cheese", purchaseCount: 2, score: 0.7 },
    { productId: "g3", name: "Ser Edam plastry", brand: null, categoryId: null, purchaseCount: 1, score: 0.64 }
  ]
});

const wholeQueue: ReviewQueue = {
  totalCount: 6,
  groups: [
    { kind: "failedReceipt", totalCount: 1, items: [item({ kind: "failedReceipt", id: "failedReceipt:r0", receiptId: "r0" })] },
    {
      kind: "totalMismatch",
      totalCount: 2,
      items: [
        item({ kind: "totalMismatch", id: "totalMismatch:p1", title: "Lidl", receiptId: "r1", purchaseId: "p1", currency: "PLN", receiptTotal: 48.2, linesTotal: 45.7 }),
        item({ kind: "totalMismatch", id: "totalMismatch:p2", title: "Żabka", purchaseId: "p2", currency: "PLN", receiptTotal: 20, linesTotal: 15 })
      ]
    },
    { kind: "productNeedsReview", totalCount: 1, items: [pendingGouda] },
    { kind: "suggestedCategory", totalCount: 1, items: [item({ kind: "suggestedCategory", id: "suggestedCategory:j1", title: "Sok jabłkowy Kubuś 0,3 l", productId: "j1", categoryId: "juice", categoryName: "Соки" })] },
    { kind: "missingCategory", totalCount: 1, items: [item({ kind: "missingCategory", id: "missingCategory:y1", title: "Jogurt Danio waniliowy", productId: "y1" })] }
  ]
};

// What «Для ребёнка» leaves: items about tagged products only; the failed receipt and the unmatched line go.
const kidQueue: ReviewQueue = {
  totalCount: 3,
  groups: [
    { kind: "totalMismatch", totalCount: 1, items: [wholeQueue.groups[1].items[0]] },
    wholeQueue.groups[3],
    wholeQueue.groups[4]
  ]
};

// What «Без категории» leaves: the product nobody classified and a purchase with such a line.
const uncategorizedQueue: ReviewQueue = {
  totalCount: 2,
  groups: [
    { kind: "totalMismatch", totalCount: 1, items: [wholeQueue.groups[1].items[1]] },
    wholeQueue.groups[4]
  ]
};

const emptyQueue: ReviewQueue = { totalCount: 0, groups: [] };

const suggestedCategory = (id: string, title: string, categoryName: string) =>
  item({ kind: "suggestedCategory", id: `suggestedCategory:${id}`, title, productId: id, categoryId: "juice", categoryName });

/** The whole hub: every section, most important first, with the server's summary. */
const hubQueue: ReviewQueue = {
  totalCount: 13,
  groups: [
    wholeQueue.groups[0],
    wholeQueue.groups[1],
    { kind: "productNeedsReview", totalCount: 1, items: [pendingGouda] },
    {
      kind: "suggestedCategory",
      totalCount: 4,
      items: [
        wholeQueue.groups[3].items[0],
        suggestedCategory("j2", "Sok pomarańczowy Tymbark 1 l", "Соки"),
        suggestedCategory("j3", "Nektar wieloowocowy Hortex 1 l", "Соки"),
        suggestedCategory("j4", "Woda kokosowa 330 ml", "Соки")
      ]
    },
    wholeQueue.groups[4],
    {
      kind: "unapprovedLines",
      totalCount: 2,
      items: [
        item({ kind: "unapprovedLines", id: "unapprovedLines:p1", title: "Lidl", purchaseId: "p1", currency: "PLN", affectedLineCount: 12, lineCount: 14, itemPosition: 2 }),
        item({ kind: "unapprovedLines", id: "unapprovedLines:p3", title: "Biedronka", purchaseId: "p3", currency: "PLN", affectedLineCount: 3, lineCount: 3, itemPosition: 0 })
      ]
    },
    {
      kind: "unratedPurchase",
      totalCount: 2,
      items: [
        item({ kind: "unratedPurchase", id: "unratedPurchase:l1", title: "Jogurt Danio waniliowy", purchaseId: "p1", productId: "y1", purchaseItemId: "l1", merchantName: "Lidl", currency: "PLN", price: 2.79 }),
        item({ kind: "unratedPurchase", id: "unratedPurchase:l2", title: "Sok jabłkowy Kubuś 0,3 l", purchaseId: "p1", productId: "j1", purchaseItemId: "l2", merchantName: "Lidl", currency: "PLN", price: 3.49 })
      ]
    }
  ],
  summary: {
    totalCount: 13,
    sections: [
      { section: "receipts", count: 3 },
      { section: "purchases", count: 2 },
      { section: "products", count: 1 },
      { section: "categories", count: 5 },
      { section: "ratings", count: 2 }
    ]
  }
};

const rememberedCodes = [
  { code: "5900334012345", rememberedAt: Date.UTC(2026, 9, 27, 15, 20), off: { name: "Kefir naturalny", brand: "Piątnica", quantity: "400 g", imageUrl: null } },
  { code: "4006040123456", rememberedAt: Date.UTC(2026, 9, 26, 9, 5), off: null }
];

/** Stories keep the selects working, as on the real page, so Controls only set the starting filters. */
function Interactive(props: ReviewQueueViewProps) {
  const [category, setCategory] = useState(props.category);
  const [tag, setTag] = useState(props.tag);
  const queue = category === props.category && tag === props.tag
    ? props.queue
    : category === "" && tag === "" ? wholeQueue
      : category === "none" && tag === "" ? uncategorizedQueue
        : category === "" && tag === "kid" ? kidQueue
          : emptyQueue;
  return (
    <ReviewQueueView
      {...props}
      queue={queue}
      category={category}
      tag={tag}
      onCategoryChange={(value) => { setCategory(value); props.onCategoryChange(value); }}
      onTagChange={(value) => { setTag(value); props.onTagChange(value); }}
      onResetFilters={() => { setCategory(""); setTag(""); props.onResetFilters(); }}
    />
  );
}

const meta = {
  title: "Purchase Memory/Review queue/Filters and suggestions",
  component: ReviewQueueView,
  parameters: { layout: "fullscreen" },
  decorators: [(Story) => <MemoryRouter><Story /></MemoryRouter>],
  render: (args) => <Interactive {...args} />,
  args: {
    queue: wholeQueue,
    loading: false,
    error: false,
    onRetry: fn(),
    categories: sampleCategories,
    category: "",
    onCategoryChange: fn(),
    tags: householdTags,
    tag: "",
    onTagChange: fn(),
    onResetFilters: fn()
  }
} satisfies Meta<typeof ReviewQueueView>;

export default meta;
type Story = StoryObj<typeof meta>;

export const WholeQueueDesktop: Story = {
  globals: { viewport: { value: "desktop", isRotated: false } }
};

/** «Тот же товар или новый?»: a flagged product with three candidates, a long name and one without brand or category. */
export const SuggestionsMobile: Story = {
  args: { queue: { totalCount: 1, groups: [{ kind: "productNeedsReview", totalCount: 1, items: [pendingGouda] }] } },
  globals: { viewport: { value: "phone", isRotated: false } }
};

export const SuggestionsDesktopEn: Story = {
  args: { queue: { totalCount: 1, groups: [{ kind: "productNeedsReview", totalCount: 1, items: [pendingGouda] }] } },
  globals: { viewport: { value: "desktop", isRotated: false }, locale: "en" }
};

/** A new flagged product without a category: one card with the candidates and «Выбрать категорию». */
export const NewProductWithoutCategoryMobile: Story = {
  args: { queue: { totalCount: 1, groups: [{ kind: "productNeedsReview", totalCount: 1, items: [{ ...pendingGouda, needsCategory: true }] }] } },
  globals: { viewport: { value: "phone", isRotated: false } }
};

/** A new flagged product the AI already gave a category: one card with the candidates, «Подтвердить» and «Изменить». */
export const NewProductWithSuggestedCategoryMobile: Story = {
  args: { queue: { totalCount: 1, groups: [{ kind: "productNeedsReview", totalCount: 1, items: [{ ...pendingGouda, categoryId: "cheese", categoryName: "Сыр" }] }] } },
  globals: { viewport: { value: "phone", isRotated: false } }
};

export const KidsMobile: Story = {
  args: { queue: kidQueue, tag: "kid" },
  globals: { viewport: { value: "phone", isRotated: false } }
};

export const KidsDesktopEn: Story = {
  args: { queue: kidQueue, tag: "kid" },
  globals: { viewport: { value: "desktop", isRotated: false }, locale: "en" }
};

export const UncategorizedMobile: Story = {
  args: { queue: uncategorizedQueue, category: "none" },
  globals: { viewport: { value: "phone", isRotated: false } }
};

export const CategoryAndTagEmptyMobile: Story = {
  args: { queue: emptyQueue, category: "cheese", tag: "party" },
  globals: { viewport: { value: "phone", isRotated: false } }
};

/** No tags in the household: only the category filter is shown. */
export const NoTagsMobile: Story = {
  args: { tags: [] },
  globals: { viewport: { value: "phone", isRotated: false } }
};

/** The «Ревью» hub with every section, the device's remembered barcodes and «Принять все предложенные». */
export const HubAllSectionsMobile: Story = {
  args: { queue: hubQueue, barcodes: rememberedCodes },
  globals: { viewport: { value: "phone", isRotated: false } }
};

export const HubAllSectionsMobileEn: Story = {
  args: { queue: hubQueue, barcodes: rememberedCodes },
  globals: { viewport: { value: "phone", isRotated: false }, locale: "en" }
};

/** «Категории» opened with «Показать все». */
export const HubCategoriesOpenMobile: Story = {
  args: { queue: hubQueue, openSections: ["categories"] },
  globals: { viewport: { value: "phone", isRotated: false } }
};

export const HubAllCheckedMobile: Story = {
  args: { queue: { totalCount: 0, groups: [], summary: { totalCount: 0, sections: [] } } },
  globals: { viewport: { value: "phone", isRotated: false } }
};

/** «Проверять по одной»: «Категории» card by card. */
export const CardsCategoriesMobile: Story = {
  render: () => <ReviewCardsView section="categories" queue={hubQueue} loading={false} error={false} onRetry={fn()} categories={sampleCategories} />,
  globals: { viewport: { value: "phone", isRotated: false } }
};

export const CardsProductsMobile: Story = {
  render: () => <ReviewCardsView section="products" queue={hubQueue} loading={false} error={false} onRetry={fn()} categories={sampleCategories} />,
  globals: { viewport: { value: "phone", isRotated: false } }
};

export const CardsRatingsMobileEn: Story = {
  render: () => <ReviewCardsView section="ratings" queue={hubQueue} loading={false} error={false} onRetry={fn()} categories={sampleCategories} />,
  globals: { viewport: { value: "phone", isRotated: false }, locale: "en" }
};
