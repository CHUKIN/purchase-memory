import type { Meta, StoryObj } from "@storybook/react-vite";
import { fn } from "storybook/test";
import { sampleCategories } from "./categoryFixtures";
import { ProductClassification } from "./ProductClassification";

const phone = { viewport: { value: "phone", isRotated: false } };

const meta = {
  title: "Purchase Memory/Categories/ProductClassification",
  component: ProductClassification,
  parameters: { layout: "padded" },
  args: {
    categories: sampleCategories,
    categoryId: "milk",
    categorySource: "confirmed",
    brand: "Łaciate",
    brandSource: "confirmed",
    onSave: fn(async () => undefined)
  },
  argTypes: {
    categorySource: { control: "inline-radio", options: ["confirmed", "suggested", null] },
    brandSource: { control: "inline-radio", options: ["confirmed", "suggested", null] }
  }
} satisfies Meta<typeof ProductClassification>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Confirmed: Story = {};
export const Suggested: Story = { args: { categoryId: "coffee", categorySource: "suggested", brand: "Lavazza", brandSource: "suggested" } };
export const Uncategorized: Story = { args: { categoryId: null, categorySource: null, brand: null, brandSource: null } };
export const Editing: Story = { args: { initiallyEditing: true } };
export const LongNamesMobile: Story = {
  globals: phone,
  args: {
    categoryId: "own-child",
    categorySource: "suggested",
    brand: "Przedsiębiorstwo Produkcyjno-Handlowe «Wędkarz Mazowiecki»",
    brandSource: "suggested"
  }
};
export const EditingMobileEn: Story = { globals: { ...phone, locale: "en" }, args: { initiallyEditing: true } };
export const SaveFails: Story = {
  args: { categorySource: "suggested", onSave: fn(async () => Promise.reject(new Error("offline"))) }
};
