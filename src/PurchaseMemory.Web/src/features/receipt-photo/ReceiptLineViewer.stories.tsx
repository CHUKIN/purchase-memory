import type { Meta, StoryObj } from "@storybook/react-vite";
import { IonButton } from "@ionic/react";
import { useState } from "react";
import { fn } from "storybook/test";
import { ReceiptLineViewer, type LocatableLine, type ReceiptLineViewerProps } from "./ReceiptLineViewer";

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
       <text x="30" y="760" font-family="monospace" font-size="28">SUMA PLN     27,46</text>
     </svg>`
  );

const row = (top: number) => ({ x: 0.05, y: top / 900, width: 0.88, height: 34 / 900 });

const lines: LocatableLine[] = [
  { name: "MLEKO 3,2%", quantity: 1, unit: "szt", unitPrice: 4.49, lineTotal: 4.49, kind: "product", boundingBox: row(176) },
  { name: "CHLEB ŻYTNI", quantity: 1, unit: "szt", unitPrice: 6.99, lineTotal: 6.99, kind: "product", boundingBox: row(236) },
  { name: "MASŁO EXTRA", quantity: 2, unit: "szt", unitPrice: 8.99, lineTotal: 17.98, kind: "product", boundingBox: row(296) },
  { name: "OPUST", quantity: null, unit: null, unitPrice: null, lineTotal: -2, kind: "discount", boundingBox: row(346) },
  // Added by hand: recognition never placed it.
  { name: "Пакет", quantity: 1, unit: null, unitPrice: 0.5, lineTotal: 0.5, kind: "product", boundingBox: null }
];

/** Opens on load and reopens from the button, like a tapped line on a screen. */
function OpenableViewer(args: ReceiptLineViewerProps) {
  const [openIndex, setOpenIndex] = useState<number | null>(args.openIndex);
  return (
    <>
      <IonButton onClick={() => setOpenIndex(args.openIndex ?? 0)}>Open line {(args.openIndex ?? 0) + 1}</IonButton>
      <ReceiptLineViewer {...args} openIndex={openIndex} onClose={() => { args.onClose(); setOpenIndex(null); }} />
    </>
  );
}

const meta = {
  title: "Purchase Memory/ReceiptLineViewer",
  component: ReceiptLineViewer,
  render: OpenableViewer,
  parameters: { layout: "padded" },
  globals: { viewport: { value: "phone", isRotated: false } },
  args: {
    lines,
    openIndex: 1,
    onClose: fn(),
    photo: { photo: { url: receiptPhotoUrl, croppable: true }, failed: false },
    currency: "PLN",
    mode: "tap"
  },
  argTypes: {
    mode: { control: "inline-radio", options: ["tap", "swipe"] },
    openIndex: { control: { type: "number", min: 0, max: lines.length - 1 } }
  }
} satisfies Meta<typeof ReceiptLineViewer>;

export default meta;
type Story = StoryObj<typeof meta>;

/** «Нажатие»: the tapped line, outlined on its strip of the photo. */
export const TapMode: Story = {};

/** «Пролистывание»: starts at the tapped line; swipe or use the buttons. */
export const SwipeMode: Story = { args: { mode: "swipe" } };

/** A line recognition could not place: the whole photo, nothing outlined. */
export const LineWithoutBox: Story = { args: { openIndex: 4 } };

/** A receipt shot in several parts: never outlines, shows the first photo whole. */
export const MultiPartReceipt: Story = {
  args: { mode: "swipe", photo: { photo: { url: receiptPhotoUrl, croppable: false }, failed: false } }
};

export const PhotoFailed: Story = { args: { photo: { photo: null, failed: true } } };

/**
 * An iPhone turned sideways (844 × 390): the strip gets narrower, never
 * shorter than its ratio, so the outline stays on the right row.
 */
export const Landscape: Story = { globals: { viewport: { value: "phone", isRotated: true } } };
