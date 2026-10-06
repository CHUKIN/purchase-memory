import type { Meta, StoryObj } from "@storybook/react-vite";
import { fn } from "storybook/test";
import { sampleCategories } from "./categoryFixtures";
import { CategoryManager } from "./CategoryManager";

const meta = {
  title: "Purchase Memory/Categories/CategoryManager",
  component: CategoryManager,
  parameters: { layout: "padded" },
  args: {
    categories: sampleCategories,
    defaultOpen: true,
    onCreate: fn(async () => undefined),
    onRename: fn(async () => undefined),
    onDelete: fn(async () => undefined)
  }
} satisfies Meta<typeof CategoryManager>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Open: Story = {};
export const Collapsed: Story = { args: { defaultOpen: false } };
export const MobileEn: Story = { globals: { viewport: { value: "phone", isRotated: false }, locale: "en" } };
export const ActionFails: Story = {
  args: { onDelete: fn(async () => Promise.reject(new Error("Delete or move the subcategories first"))) }
};
