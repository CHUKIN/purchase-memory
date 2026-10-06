import type { Meta, StoryObj } from "@storybook/react-vite";
import { useEffect, useState } from "react";
import { fn } from "storybook/test";
import type { PurchaseDraftIssue, ReceiptReviewAuditEntry, ReceiptSnapshot } from "../../api/types";
import { IssueReview, type IssueReviewProps } from "./IssueReview";

const snapshot: ReceiptSnapshot = {
  merchantName: "Biedronka Centrum",
  purchasedAt: "2026-08-24T17:12:00Z",
  currency: "PLN",
  receiptTotal: 84.32,
  items: [
    { name: "MLEKO 3,2%", quantity: 1, unit: "pcs", unitPrice: 4.49, lineTotal: 4.49 },
    { name: "CHLEB ŻYTNI", quantity: 1, unit: "pcs", unitPrice: null, lineTotal: null },
    { name: "MASŁO EXTRA", quantity: 2, unit: "pcs", unitPrice: 8.99, lineTotal: 17.98 }
  ]
};

const mismatch: PurchaseDraftIssue = {
  type: "receiptTotalMismatch",
  itemPosition: null,
  itemName: null,
  recognizedValue: "22.47 PLN",
  suggestedFix: "Correct the receipt total or the line totals so that they match."
};

const missingPrice: PurchaseDraftIssue = {
  type: "missingItemPrice",
  itemPosition: 1,
  itemName: "CHLEB ŻYTNI",
  recognizedValue: "",
  suggestedFix: "Set the price for this item or remove it."
};

/** A mismatch card only makes sense when every line total is present. */
const completeSnapshot: ReceiptSnapshot = {
  ...snapshot,
  items: snapshot.items.map((item) =>
    item.lineTotal === null ? { ...item, unitPrice: 6.99, lineTotal: 6.99 } : item)
};

/**
 * A stand-in receipt photo. Storybook must not depend on a running API, so the
 * fragment stories crop an inline SVG instead of a stored capture.
 */
const receiptPhotoUrl =
  "data:image/svg+xml;utf8," +
  encodeURIComponent(
    `<svg xmlns="http://www.w3.org/2000/svg" width="420" height="900">
       <rect width="420" height="900" fill="#f6f3ec"/>
       <text x="30" y="120" font-family="monospace" font-size="26">BIEDRONKA</text>
       <text x="30" y="380" font-family="monospace" font-size="24">CHLEB ZYTNI      ?,??</text>
       <text x="30" y="760" font-family="monospace" font-size="28">SUMA PLN     84,32</text>
     </svg>`
  );

const photo = { url: receiptPhotoUrl, croppable: true };

const phone = { viewport: { value: "phone", isRotated: false } };
const desktop = { viewport: { value: "desktop", isRotated: false } };

/**
 * Keeps the edited snapshot in local state so that clicking a suggested fix
 * (e.g. "Use lines total") in the Canvas actually re-renders `IssueReview`
 * with the updated snapshot, letting the resolved card collapse for real
 * instead of only logging the call to the Actions panel.
 */
function InteractiveIssueReview(args: IssueReviewProps) {
  const [snapshot, setSnapshot] = useState(args.snapshot);
  return (
    <IssueReview
      {...args}
      snapshot={snapshot}
      onSnapshotChange={(next) => {
        args.onSnapshotChange(next);
        setSnapshot(next);
      }}
    />
  );
}

const meta = {
  title: "Purchase Memory/IssueReview/Production",
  component: IssueReview,
  render: InteractiveIssueReview,
  parameters: { layout: "padded" },
  args: {
    snapshot,
    onSnapshotChange: fn(),
    issues: [],
    onOpenFullEditor: fn(),
    onSave: fn()
  }
} satisfies Meta<typeof IssueReview>;

export default meta;
type Story = StoryObj<typeof meta>;

/** Defensive: the app routes to the full editor instead of rendering this. */
export const NoIssues: Story = {
  args: { snapshot: completeSnapshot, issues: [] },
  globals: phone
};

export const ReceiptLevelIssue: Story = {
  args: { snapshot: completeSnapshot, issues: [mismatch] },
  globals: phone
};

export const ItemLevelIssue: Story = {
  args: { issues: [missingPrice] },
  globals: phone
};

export const BothIssues: Story = {
  args: { snapshot: { ...snapshot, receiptTotal: 99 }, issues: [missingPrice, mismatch] },
  globals: desktop
};

/** Everything resolved locally: saving is unblocked and cards read as fixed. */
export const AllResolved: Story = {
  args: { snapshot: { ...completeSnapshot, receiptTotal: 29.46 }, issues: [mismatch] },
  globals: desktop
};

/** Recognition placed the total, so the card shows just that row of the photo. */
export const PhotoFragment: Story = {
  args: {
    snapshot: completeSnapshot,
    issues: [{ ...mismatch, boundingBox: { x: 0.05, y: 0.8, width: 0.62, height: 0.045 } }],
    photo
  },
  globals: phone
};

/** No coordinates, or a receipt shot in several parts: the whole photo is shown. */
export const PhotoFallback: Story = {
  args: { snapshot: completeSnapshot, issues: [mismatch], photo },
  globals: phone
};

/** The escape hatch to the untouched full editor, in English. */
export const FullEditorHandoff: Story = {
  args: { issues: [missingPrice, mismatch] },
  globals: { ...desktop, locale: "en" }
};

const sampleHistory: ReceiptReviewAuditEntry[] = [
  {
    occurredAt: "2026-08-24T17:20:00Z",
    actor: "Owner",
    fieldName: "MerchantName",
    changeType: "Modified",
    oldValue: "Biedronka",
    newValue: "Biedronka Centrum"
  },
  {
    occurredAt: "2026-08-24T17:20:00Z",
    actor: "Owner",
    fieldName: "Item:2",
    changeType: "ItemRemoved",
    oldValue: "MLEKO 1,5% (qty=1, total=4.29)",
    newValue: null
  }
];

/**
 * Storybook has no backend, so this story stubs the one endpoint the history
 * section calls. It demonstrates the collapsed-by-default section opening to
 * show a couple of sample entries once the reviewer expands it.
 */
function WithHistoryDemo(args: IssueReviewProps) {
  useEffect(() => {
    const originalFetch = window.fetch;
    window.fetch = ((input: RequestInfo | URL) => {
      const url = typeof input === "string" ? input : input.toString();
      if (url.includes("/audit")) {
        return Promise.resolve(new Response(JSON.stringify(sampleHistory), { status: 200 }));
      }

      return originalFetch(input);
    }) as typeof window.fetch;

    return () => {
      window.fetch = originalFetch;
    };
  }, []);

  return <InteractiveIssueReview {...args} />;
}

/** Change history: collapsed by default, showing a field edit and a removed item once opened. */
export const WithHistory: Story = {
  args: { snapshot: completeSnapshot, issues: [mismatch], receiptId: "story-receipt" },
  render: WithHistoryDemo,
  globals: desktop
};
