import type { Meta, StoryObj } from "@storybook/react-vite";
import { IonButton } from "@ionic/react";
import { useState } from "react";
import { fn } from "storybook/test";
import { storyBox, storyOutlines, tiltedReceiptUrl } from "../receipt-photo/storyReceipt";
import { PurchaseLineReview, type ReviewLine, type ReviewStart } from "./PurchaseLineReview";

/** Story data only: a saved purchase on the stand-in tilted receipt. */
const lines: ReviewLine[] = [
  { id: "1", name: "MLEKO 3,2%", quantity: 1, unit: "szt", unitPrice: 4.49, lineTotal: 4.49, kind: "product", boundingBox: storyBox("milk"), textPolygons: storyOutlines("milk"), productId: "milk", productName: "Mleko 3,2%" },
  { id: "2", name: "CHLEB ŻYTNI", quantity: 1, unit: "szt", unitPrice: 6.99, lineTotal: 6.99, kind: "product", boundingBox: storyBox("bread"), textPolygons: storyOutlines("bread"), productId: "bread", productName: "Chleb żytni", productNeedsReview: true },
  { id: "3", name: "MASŁO EXTRA", quantity: 2, unit: "szt", unitPrice: null, lineTotal: 17.98, kind: "product", boundingBox: storyBox("butter"), textPolygons: storyOutlines("butter"), productId: "butter", productName: "Masło extra 200 g" },
  { id: "4", name: "OPUST", quantity: null, unit: null, unitPrice: null, lineTotal: -2, kind: "discount", boundingBox: storyBox("discount"), textPolygons: storyOutlines("discount") }
];

type StoryArgs = { checked: string[]; startAt: number | null; withPhoto: boolean };

/** Marks in story state; saving and deleting resolve at once (nothing is stored). */
function Playground({ checked, startAt, withPhoto }: StoryArgs) {
  const [marks, setMarks] = useState(() => new Set(checked));
  const [start, setStart] = useState<ReviewStart | null>({ index: startAt, nonce: 1 });
  return (
    <>
      <IonButton onClick={() => setStart((current) => ({ index: startAt, nonce: (current?.nonce ?? 0) + 1 }))}>Open the review</IonButton>
      <PurchaseLineReview
        start={start}
        onClose={() => setStart(null)}
        lines={lines}
        isApproved={(line) => marks.has(line.id)}
        setApproved={(id, value) => setMarks((current) => {
          const next = new Set(current);
          if (value) next.add(id); else next.delete(id);
          return next;
        })}
        onSaveLine={fn(async () => undefined)}
        onRemoveLine={fn(async () => undefined)}
        photo={withPhoto ? { photo: { url: tiltedReceiptUrl, croppable: true }, failed: false } : null}
        currency="PLN"
        receiptTotal={27.46}
        onOpenProduct={fn()}
      />
    </>
  );
}

const meta = {
  title: "Purchase Memory/PurchaseLineReview",
  component: Playground,
  parameters: { layout: "fullscreen" },
  globals: { viewport: { value: "phone", isRotated: false } },
  args: { checked: [], startAt: null, withPhoto: true }
} satisfies Meta<typeof Playground>;

export default meta;
type Story = StoryObj<typeof meta>;

/** The header button: opens at the line without a price; the product and its photos under the line. */
export const FromHeader: Story = {};

/** A tapped line that is already checked. */
export const CheckedLine: Story = { args: { startAt: 0, checked: ["1"] } };

/** A purchase saved without a receipt photo. */
export const NoPhoto: Story = { args: { startAt: 0, withPhoto: false } };
