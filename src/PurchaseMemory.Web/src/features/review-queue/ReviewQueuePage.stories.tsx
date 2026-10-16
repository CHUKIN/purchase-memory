import type { Meta, StoryObj } from "@storybook/react-vite";
import { useState } from "react";
import { MemoryRouter } from "react-router";
import { fn } from "storybook/test";
import type { ReviewQueue, ReviewQueueItem, TagSummary } from "../../api/types";
import { sampleCategories } from "../categories/categoryFixtures";
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
