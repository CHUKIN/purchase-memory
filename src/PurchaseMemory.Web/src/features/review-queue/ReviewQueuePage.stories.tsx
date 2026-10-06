import type { Meta, StoryObj } from "@storybook/react-vite";
import { useState } from "react";
import { MemoryRouter } from "react-router";
import { fn } from "storybook/test";
import type { ReviewQueue, ReviewQueueItem, TagSummary } from "../../api/types";
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
    ...overrides
  };
}

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
    { kind: "pendingMatch", totalCount: 1, items: [item({ kind: "pendingMatch", id: "pendingMatch:m1", title: "Ser Gouda plastry 250g", purchaseId: "p3", productId: "g1", itemPosition: 2 })] },
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

const emptyQueue: ReviewQueue = { totalCount: 0, groups: [] };

/** Stories keep the select working, as on the real page, so Controls only set the starting tag. */
function Interactive(props: ReviewQueueViewProps) {
  const [tag, setTag] = useState(props.tag);
  const queue = tag === props.tag ? props.queue : tag === "" ? wholeQueue : tag === "kid" ? kidQueue : emptyQueue;
  return <ReviewQueueView {...props} queue={queue} tag={tag} onTagChange={(value) => { setTag(value); props.onTagChange(value); }} />;
}

const meta = {
  title: "Purchase Memory/Review queue/Tag filter",
  component: ReviewQueueView,
  parameters: { layout: "fullscreen" },
  decorators: [(Story) => <MemoryRouter><Story /></MemoryRouter>],
  render: (args) => <Interactive {...args} />,
  args: {
    queue: wholeQueue,
    loading: false,
    error: false,
    onRetry: fn(),
    tags: householdTags,
    tag: "",
    onTagChange: fn()
  }
} satisfies Meta<typeof ReviewQueueView>;

export default meta;
type Story = StoryObj<typeof meta>;

export const WholeQueueDesktop: Story = {
  globals: { viewport: { value: "desktop", isRotated: false } }
};

export const KidsMobile: Story = {
  args: { queue: kidQueue, tag: "kid" },
  globals: { viewport: { value: "phone", isRotated: false } }
};

export const KidsDesktopEn: Story = {
  args: { queue: kidQueue, tag: "kid" },
  globals: { viewport: { value: "desktop", isRotated: false }, locale: "en" }
};

export const FilteredEmptyMobile: Story = {
  args: { queue: emptyQueue, tag: "party" },
  globals: { viewport: { value: "phone", isRotated: false } }
};

/** No tags in the household: the queue looks exactly as before the filter existed. */
export const NoTagsMobile: Story = {
  args: { tags: [] },
  globals: { viewport: { value: "phone", isRotated: false } }
};
