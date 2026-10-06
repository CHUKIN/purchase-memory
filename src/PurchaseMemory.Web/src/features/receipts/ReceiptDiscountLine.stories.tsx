import type { Meta, StoryObj } from "@storybook/react-vite";
import { fn } from "storybook/test";
import { ReceiptDiscountLine } from "./ReceiptDiscountLine";

const phone = { viewport: { value: "phone", isRotated: false } };
const desktop = { viewport: { value: "desktop", isRotated: false } };

const meta = {
  title: "Purchase Memory/ReceiptDiscountLine/Production",
  component: ReceiptDiscountLine,
  parameters: { layout: "padded" },
  args: {
    name: "OPUST",
    lineTotal: -30,
    currency: "PLN",
    productName: "Odkurzacz bezprzewodowy",
    productLineTotal: 99.99
  }
} satisfies Meta<typeof ReceiptDiscountLine>;

export default meta;
type Story = StoryObj<typeof meta>;

/** A discount under its product, read only, with the real price after the discount. */
export const LinkedMobileRu: Story = { globals: phone };

export const LinkedDesktopEn: Story = { globals: { ...desktop, locale: "en" } };

/** Editable in the full review editor: the size is typed as a positive number and stored negative. */
export const EditableMobileRu: Story = { globals: phone, args: { onChange: fn(), onDelete: fn() } };

/** A discount with no product above it: kept, but flagged as not attached. */
export const UnlinkedDesktopRu: Story = {
  globals: desktop,
  args: { name: "RABAT KARTA", lineTotal: -2.5, productName: null, productLineTotal: null }
};

export const UnreadableAmountMobileEn: Story = {
  globals: { ...phone, locale: "en" },
  args: { lineTotal: null, onChange: fn(), onDelete: fn() }
};

/** A discount on the whole receipt ("OPUST (podsuma)"): a normal case, no product and no warning. */
export const WholeReceiptMobileRu: Story = {
  globals: phone,
  args: { name: "OPUST (podsuma)", lineTotal: -21.59, productName: null, productLineTotal: null, appliesToReceipt: true }
};

export const WholeReceiptDesktopEn: Story = {
  globals: { ...desktop, locale: "en" },
  args: { name: "OPUST (podsuma)", lineTotal: -21.59, productName: null, productLineTotal: null, appliesToReceipt: true }
};
