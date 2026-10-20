import type { Meta, StoryObj } from "@storybook/react-vite";
import { useState } from "react";
import { fn } from "storybook/test";
import type { LineViewerPhoto } from "../receipt-photo/LinePhoto";
import { storyBox, storyOutlines, tiltedReceiptUrl } from "../receipt-photo/storyReceipt";
import type { EditableLine } from "../receipts/LinesEditor";
import { LineCarousel } from "./LineCarousel";
import { firstProblemIndex } from "./lineChecks";

/*
 * Story data only: the stand-in tilted receipt (Storybook must not depend on
 * a running API or on owner photos) and the lines printed on it, with the
 * word outlines the server would send.
 */
const lines: EditableLine[] = [
  { name: "MLEKO 3,2%", quantity: 1, unit: "szt", unitPrice: 4.49, lineTotal: 4.49, kind: "product", boundingBox: storyBox("milk"), textPolygons: storyOutlines("milk") },
  { name: "CHLEB ŻYTNI", quantity: 1, unit: "szt", unitPrice: 6.99, lineTotal: 6.99, kind: "product", boundingBox: storyBox("bread"), textPolygons: storyOutlines("bread") },
  // Read as 8,89 instead of 17,98: quantity × price does not match.
  { name: "MASŁO EXTRA", quantity: 2, unit: "szt", unitPrice: 8.99, lineTotal: 8.89, kind: "product", boundingBox: storyBox("butter") },
  { name: "OPUST", quantity: null, unit: null, unitPrice: null, lineTotal: -2, kind: "discount", boundingBox: storyBox("discount"), textPolygons: storyOutlines("discount") },
  {
    name: "SER GOUDA PLASTRY MLECZARNIA 250G", quantity: 1, unit: "szt", unitPrice: 9.99, lineTotal: 9.99, kind: "product",
    boundingBox: storyBox("goudaName", 50), textPolygons: storyOutlines("goudaName", "goudaPrice")
  },
  // Added by hand: recognition never placed it.
  { name: "Пакет", quantity: 1, unit: null, unitPrice: 0.5, lineTotal: 0.5, kind: "product", boundingBox: null }
];
const fixed = lines.map((line, index) => index === 2 ? { ...line, lineTotal: 17.98 } : line);
const photo: LineViewerPhoto = { photo: { url: tiltedReceiptUrl, croppable: true }, failed: false };

interface StoryArgs {
  lines: EditableLine[];
  receiptTotal: number | null;
  photo: LineViewerPhoto | null;
  startAt: number | null;
  checked: number[];
  onApprove: (index: number) => void;
  onRemove: (index: number) => void;
}

/** Keeps lines, position and marks in story state, like the capture screen does. */
function Playground({ lines: initialLines, receiptTotal, photo: shownPhoto, startAt, checked: initialChecked, onApprove, onRemove }: StoryArgs) {
  const [items, setItems] = useState(initialLines);
  const [index, setIndex] = useState(startAt ?? firstProblemIndex(initialLines));
  const [checked, setChecked] = useState(() => new Set(initialChecked));
  return (
    <div style={{ background: "var(--pm-grouped-background)", minHeight: "100vh", paddingTop: 8 }}>
      <LineCarousel
        lines={items}
        index={index}
        onIndexChange={setIndex}
        onChange={(position, patch) => setItems((current) => current.map((line, other) => other === position ? { ...line, ...patch } : line))}
        onRemove={(position) => {
          onRemove(position);
          setItems((current) => current.filter((_, other) => other !== position));
          setIndex((current) => Math.max(0, Math.min(current, items.length - 2)));
        }}
        isChecked={(position) => checked.has(position)}
        onApprove={(position) => { onApprove(position); setChecked((current) => new Set(current).add(position)); }}
        onUnapprove={(position) => setChecked((current) => { const next = new Set(current); next.delete(position); return next; })}
        currency="PLN"
        receiptTotal={receiptTotal}
        photo={shownPhoto}
      />
    </div>
  );
}

const meta = {
  title: "Purchase Memory/LineCarousel",
  component: Playground,
  parameters: { layout: "fullscreen" },
  globals: { viewport: { value: "phone", isRotated: false } },
  args: {
    lines,
    receiptTotal: 37.95,
    photo,
    startAt: null,
    checked: [],
    onApprove: fn(),
    onRemove: fn()
  },
  argTypes: {
    startAt: { control: { type: "number", min: 0, max: lines.length - 1 }, description: "Line to show; empty = first line with a problem" }
  }
} satisfies Meta<typeof Playground>;

export default meta;
type Story = StoryObj<typeof meta>;

/** Opens at the problem line: «2 × 8,99 = 17,98, а сумма 8,89», the lines 9,09 short of the total (390 × 844). */
export const WithProblem: Story = {};

/** Every line checked and adding up: green status, «Проверено» instead of «Верно, дальше», green dots. */
export const AllChecked: Story = { args: { lines: fixed, startAt: 0, checked: fixed.map((_, index) => index) } };

/** A line recognition could not place: the whole photo, nothing highlighted, a short note. */
export const NoBoxFallback: Story = { args: { lines: fixed, startAt: 5 } };

/** A discount: its name and amount only, and the product it belongs to. */
export const Discount: Story = { args: { lines: fixed, startAt: 3 } };

/** Name on one row, price on the next: both rows highlighted, the window fits both. */
export const TwoRowLine: Story = { args: { lines: fixed, startAt: 4 } };

/** A receipt in several photos: the first one whole, no highlight. */
export const MultiPhoto: Story = { args: { lines: fixed, startAt: 0, photo: { photo: { url: tiltedReceiptUrl, croppable: false }, failed: false } } };

/** No receipt photo: the lines are still checked and edited. */
export const NoPhoto: Story = { args: { lines: fixed, startAt: 0, photo: null } };

/** The receipt total was not read: the status says so. */
export const NoReceiptTotal: Story = { args: { lines: fixed, startAt: 0, receiptTotal: null } };

/** An iPhone SE (375 × 667). */
export const SmallPhone: Story = { globals: { viewport: { value: "phoneSmall", isRotated: false } } };
