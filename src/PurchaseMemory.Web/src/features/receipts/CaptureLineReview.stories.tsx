import type { Meta, StoryObj } from "@storybook/react-vite";
import { ChevronRight } from "lucide-react";
import { useState } from "react";
import { fn } from "storybook/test";
import type { ReceiptSnapshot } from "../../api/types";
import { Page, PrimaryButton } from "../../design-system/components";
import { stepLinePosition } from "../line-review/carouselSteps";
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

/**
 * The draft of the approved mock: two weighed products, each with its
 * «Opis: Rabat» discount; the receipt total already has the discounts in
 * the price, so the summary does not match.
 */
const pierogi: ReceiptSnapshot = {
  merchantName: "Produkty Mrożone",
  purchasedAt: "2026-10-16T13:48:00Z",
  currency: "PLN",
  receiptTotal: 88.83,
  items: [
    { name: "PIEROGI Z ZIEMNIAKAMI I MASŁEM", quantity: 1.165, unit: "kg", unitPrice: 38.25, lineTotal: 44.56, kind: "product", boundingBox: null },
    { name: "Opis: Rabat", quantity: null, unit: null, unitPrice: null, lineTotal: -7.86, kind: "discount", boundingBox: null },
    { name: "PIELMIENI Z KURCZAKIEM", quantity: 1.085, unit: "kg", unitPrice: 40.8, lineTotal: 44.27, kind: "product", boundingBox: null },
    { name: "Opis: Rabat", quantity: null, unit: null, unitPrice: null, lineTotal: -7.81, kind: "discount", boundingBox: null }
  ]
};

/**
 * The «Чек распознан» screen as the app lays it out: the carousel with
 * «Магазин, дата и итог» and «Сохранить сейчас» in the bottom bar, then the
 * summary with «Сохранить покупку».
 */
function Screen({ snapshot: initial, startInSummary = false, onSave, onOpenFullEditor }: { snapshot: ReceiptSnapshot; startInSummary?: boolean; onSave: () => void; onOpenFullEditor: () => void }) {
  const [snapshot, setSnapshot] = useState(initial);
  const [index, setIndex] = useState(() => stepLinePosition(initial.items, firstProblemIndex(initial.items)));
  const [checked, setChecked] = useState(() => initial.items.map(() => startInSummary));
  const [summary, setSummary] = useState(startInSummary);
  return (
    <Page
      title="Чек распознан"
      largeTitle={false}
      footer={summary
        ? <PrimaryButton onClick={onSave}>Сохранить покупку</PrimaryButton>
        : (
          <div className="pm-footer-links">
            <button type="button" className="pm-footer-link" onClick={onOpenFullEditor}>Магазин, дата и итог</button>
            <button type="button" className="pm-footer-link" onClick={() => setSummary(true)}>Сохранить сейчас<ChevronRight size={16} aria-hidden="true" /></button>
          </div>
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
          setIndex((current) => current > position ? current - 1 : Math.max(0, Math.min(current, snapshot.items.length - 2)));
        }}
        onRestoreItem={(position, item) => {
          setSnapshot((current) => ({ ...current, items: [...current.items.slice(0, position), item, ...current.items.slice(position)] }));
          setChecked((current) => [...current.slice(0, position), false, ...current.slice(position)]);
          setIndex((current) => current >= position ? current + 1 : current);
        }}
        checked={checked}
        onCheckedChange={(positions, value) => setChecked((current) => current.map((mark, other) => positions.includes(other) ? value : mark))}
        onUseLinesTotal={(receiptTotal) => setSnapshot((current) => ({ ...current, receiptTotal }))}
        remainingIssues={null}
        photo={{ photo: { url: tiltedReceiptUrl, croppable: true }, failed: false }}
        receiptId="story-receipt"
        summary={summary}
        onShowSummary={() => setSummary(true)}
        onBackToProducts={() => setSummary(false)}
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

/** «Чек распознан» on a 390 × 844 iPhone: opens at the product whose sum does not match; «OPUST» sits in MASŁO's card. */
export const Recognized: Story = {};

/** The approved mock: «Товар 1 из 2», the discount inside the card with «Убрать», «Верно, дальше» → «Верно, к итогу». */
export const DiscountsInCards: Story = { args: { snapshot: pierogi } };

/** The summary of the mock's receipt: lines 73,16 against the printed 88,83 — the warning box. */
export const SummaryMismatch: Story = { args: { snapshot: pierogi, startInSummary: true } };

/** The summary when the lines add up: «Сходится с итогом на чеке». */
export const SummaryMatches: Story = { args: { snapshot: { ...pierogi, receiptTotal: 73.16 }, startInSummary: true } };

/** Every line adds up: the status turns green. */
export const AddsUp: Story = { args: { snapshot: { ...draft, items: draft.items.map((item, index) => index === 2 ? { ...item, lineTotal: 17.98 } : item) } } };
