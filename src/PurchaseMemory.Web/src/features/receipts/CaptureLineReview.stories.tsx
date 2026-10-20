import type { Meta, StoryObj } from "@storybook/react-vite";
import { ChevronRight } from "lucide-react";
import { useState } from "react";
import { fn } from "storybook/test";
import type { ReceiptSnapshot } from "../../api/types";
import { Page, PrimaryButton } from "../../design-system/components";
import { firstProblemIndex } from "../line-review/lineChecks";
import { storyBox, storyOutlines, tiltedReceiptUrl } from "../receipt-photo/storyReceipt";
import { CaptureLineReview } from "./CaptureLineReview";

/** Story data only: the stand-in tilted receipt and its recognized draft. */
const draft: ReceiptSnapshot = {
  merchantName: "Biedronka",
  purchasedAt: "2026-10-16T15:48:00Z",
  currency: "PLN",
  receiptTotal: 37.95,
  items: [
    { name: "MLEKO 3,2%", quantity: 1, unit: "szt", unitPrice: 4.49, lineTotal: 4.49, kind: "product", boundingBox: storyBox("milk"), textPolygons: storyOutlines("milk") },
    { name: "CHLEB ŻYTNI", quantity: 1, unit: "szt", unitPrice: 6.99, lineTotal: 6.99, kind: "product", boundingBox: storyBox("bread"), textPolygons: storyOutlines("bread") },
    { name: "MASŁO EXTRA", quantity: 2, unit: "szt", unitPrice: 8.99, lineTotal: 8.89, kind: "product", boundingBox: storyBox("butter"), textPolygons: storyOutlines("butter") },
    { name: "OPUST", quantity: null, unit: null, unitPrice: null, lineTotal: -2, kind: "discount", boundingBox: storyBox("discount"), textPolygons: storyOutlines("discount") },
    { name: "SER GOUDA PLASTRY MLECZARNIA 250G", quantity: 1, unit: "szt", unitPrice: 9.99, lineTotal: 9.99, kind: "product", boundingBox: storyBox("goudaName", 50), textPolygons: storyOutlines("goudaName", "goudaPrice") }
  ]
};

/** The «Чек распознан» screen as the app lays it out: the carousel, and the bottom bar with «Сохранить покупку». */
function Screen({ snapshot: initial, onSave, onOpenFullEditor }: { snapshot: ReceiptSnapshot; onSave: () => void; onOpenFullEditor: () => void }) {
  const [snapshot, setSnapshot] = useState(initial);
  const [index, setIndex] = useState(() => firstProblemIndex(initial.items));
  const [checked, setChecked] = useState(() => initial.items.map(() => false));
  return (
    <Page
      title="Чек распознан"
      footer={(
        <>
          <button type="button" className="pm-footer-link" onClick={onOpenFullEditor}>Магазин, дата и итог<ChevronRight size={16} aria-hidden="true" /></button>
          <PrimaryButton onClick={onSave}>Сохранить покупку</PrimaryButton>
        </>
      )}
    >
      <CaptureLineReview
        snapshot={snapshot}
        index={index}
        onIndexChange={setIndex}
        onChangeItem={(position, patch) => setSnapshot((current) => ({ ...current, items: current.items.map((item, other) => other === position ? { ...item, ...patch } : item) }))}
        onRemoveItem={(position) => {
          setSnapshot((current) => ({ ...current, items: current.items.filter((_, other) => other !== position) }));
          setChecked((current) => current.filter((_, other) => other !== position));
          setIndex((current) => Math.max(0, Math.min(current, snapshot.items.length - 2)));
        }}
        checked={checked}
        onCheckedChange={(position, value) => setChecked((current) => current.map((mark, other) => other === position ? value : mark))}
        onUseLinesTotal={(receiptTotal) => setSnapshot((current) => ({ ...current, receiptTotal }))}
        remainingIssues={null}
        photo={{ photo: { url: tiltedReceiptUrl, croppable: true }, failed: false }}
        receiptId="story-receipt"
      />
    </Page>
  );
}

const meta = {
  title: "Purchase Memory/CaptureLineReview",
  component: Screen,
  parameters: { layout: "fullscreen" },
  globals: { viewport: { value: "phone", isRotated: false } },
  args: { snapshot: draft, onSave: fn(), onOpenFullEditor: fn() }
} satisfies Meta<typeof Screen>;

export default meta;
type Story = StoryObj<typeof meta>;

/** «Чек распознан» on a 390 × 844 iPhone: opens at the line whose sum does not match. */
export const Recognized: Story = {};

/** Every line adds up: the status turns green. */
export const AddsUp: Story = { args: { snapshot: { ...draft, items: draft.items.map((item, index) => index === 2 ? { ...item, lineTotal: 17.98 } : item) } } };
