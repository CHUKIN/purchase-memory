import type { Meta, StoryObj } from "@storybook/react-vite";
import { IonButton } from "@ionic/react";
import { useState } from "react";
import { fn } from "storybook/test";
import { LineReview, type LineReviewProps, type ReviewLine } from "./LineReview";

/**
 * A stand-in receipt photo (Storybook must not depend on a running API). The
 * boxes below are where these rows are drawn, as fractions of 420×900.
 */
const receiptPhotoUrl =
  "data:image/svg+xml;utf8," +
  encodeURIComponent(
    `<svg xmlns="http://www.w3.org/2000/svg" width="420" height="900">
       <rect width="420" height="900" fill="#f6f3ec"/>
       <text x="30" y="90" font-family="monospace" font-size="26">BIEDRONKA</text>
       <text x="30" y="200" font-family="monospace" font-size="22">MLEKO 3,2%     4,49</text>
       <text x="30" y="260" font-family="monospace" font-size="22">CHLEB ZYTNI    6,99</text>
       <text x="30" y="320" font-family="monospace" font-size="22">MASLO EXTRA   17,98</text>
       <text x="30" y="370" font-family="monospace" font-size="22">  OPUST       -2,00</text>
       <text x="30" y="430" font-family="monospace" font-size="16">SER GOUDA PLASTRY</text>
       <text x="30" y="452" font-family="monospace" font-size="16">  MLECZARNIA 250G   9,99</text>
       <text x="30" y="760" font-family="monospace" font-size="28">SUMA PLN     37,45</text>
     </svg>`
  );

const row = (top: number, height = 34) => ({ x: 0.05, y: top / 900, width: 0.88, height: height / 900 });

const lines: ReviewLine[] = [
  { id: "1", name: "MLEKO 3,2%", quantity: 1, unit: "szt", unitPrice: 4.49, lineTotal: 4.49, kind: "product", boundingBox: row(176), productName: "Mleko 3,2%" },
  { id: "2", name: "CHLEB ŻYTNI", quantity: 1, unit: "szt", unitPrice: 6.99, lineTotal: 6.99, kind: "product", boundingBox: row(236), pendingMatch: true },
  { id: "3", name: "MASŁO EXTRA", quantity: 2, unit: "szt", unitPrice: 8.99, lineTotal: 17.98, kind: "product", boundingBox: row(296), productName: "Masło extra 200 g" },
  { id: "4", name: "OPUST", quantity: null, unit: null, unitPrice: null, lineTotal: -2, kind: "discount", boundingBox: row(346) },
  {
    id: "5", name: "SER GOUDA PLASTRY MLECZARNIA REGIONALNA WĘDZONY 250G ×2 PROMOCJA", quantity: 1, unit: "szt", unitPrice: 9.99, lineTotal: 9.99,
    kind: "product", boundingBox: row(410, 50), productName: "Ser gouda plastry 250 g"
  },
  // Added by hand: recognition never placed it.
  { id: "6", name: "Пакет", quantity: 1, unit: null, unitPrice: 0.5, lineTotal: 0.5, kind: "product", boundingBox: null }
];

type StoryArgs = LineReviewProps & { startAt: number | null; checked: string[] };

/** Keeps the marks in story state and reopens from the button, like the purchase page. */
function Playground({ startAt, checked, ...args }: StoryArgs) {
  const [marks, setMarks] = useState(() => new Set(checked));
  const [start, setStart] = useState<LineReviewProps["start"]>({ index: startAt, nonce: 1 });
  return (
    <>
      <IonButton onClick={() => setStart((current) => ({ index: startAt, nonce: (current?.nonce ?? 0) + 1 }))}>Open the review</IonButton>
      <LineReview
        {...args}
        start={start}
        onClose={() => { args.onClose(); setStart(null); }}
        isApproved={(line) => marks.has(line.id)}
        setApproved={(id, value) => {
          args.setApproved(id, value);
          setMarks((current) => {
            const next = new Set(current);
            if (value) next.add(id); else next.delete(id);
            return next;
          });
        }}
      />
    </>
  );
}

const meta = {
  title: "Purchase Memory/LineReview",
  component: Playground,
  parameters: { layout: "fullscreen" },
  globals: { viewport: { value: "phoneSmall", isRotated: false } },
  args: {
    lines,
    start: null,
    onClose: fn(),
    isApproved: () => false,
    setApproved: fn(),
    onSaveLine: fn(async () => undefined),
    photo: { photo: { url: receiptPhotoUrl, croppable: true }, failed: false },
    currency: "PLN",
    startAt: 0,
    checked: []
  },
  argTypes: {
    startAt: { control: { type: "number", min: 0, max: lines.length - 1 }, description: "Tapped line; empty = first unchecked" }
  }
} satisfies Meta<typeof Playground>;

export default meta;
type Story = StoryObj<typeof meta>;

/** A product line, outlined on the photo, with its product link (iPhone SE, 375 × 667). */
export const Normal: Story = {};

/** A line recognition could not place: the whole photo, nothing outlined, a short note. */
export const NoBox: Story = { args: { startAt: 5 } };

/** A long name wraps to two lines and the card still fits without scrolling. */
export const LongName: Story = { args: { startAt: 4 } };

/** A product with its discount and the price after it; the discount line itself is the next card. */
export const WithDiscount: Story = { args: { startAt: 2 } };

/** Zoomed: double-tap or pinch the photo (or the corner button); swiping is off until zoomed out. */
export const Zoomed: Story = {
  play: async ({ canvasElement }) => {
    const button = canvasElement.ownerDocument.querySelector<HTMLButtonElement>(".pm-photo-zoom");
    button?.click();
  }
};

/** Every line checked: the short «all checked» state with «Проверить ещё раз». */
export const AllChecked: Story = { args: { startAt: null, checked: lines.map((line) => line.id) } };

/** No receipt photo: the review still works on the line details. */
export const NoPhoto: Story = { args: { photo: null } };

/** A phone on its side (812 × 375): photo and line side by side, buttons in a column. */
export const Landscape: Story = { globals: { viewport: { value: "phoneLandscape", isRotated: false } } };

/** The same on a regular iPhone (390 × 844). */
export const LargerPhone: Story = { globals: { viewport: { value: "phone", isRotated: false } } };
