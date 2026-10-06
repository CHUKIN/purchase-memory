import type { Meta, StoryObj } from "@storybook/react-vite";
import { CategoryChip } from "./CategoryChip";

const meta = {
  title: "Purchase Memory/Categories/CategoryChip",
  component: CategoryChip,
  parameters: { layout: "padded" },
  args: { label: "Молочное и яйца / Молоко", source: "confirmed" },
  argTypes: { source: { control: "inline-radio", options: ["confirmed", "suggested", null] } }
} satisfies Meta<typeof CategoryChip>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Confirmed: Story = {};
export const Suggested: Story = { args: { source: "suggested" } };
export const None: Story = { args: { label: null, source: null } };
export const SuggestedEn: Story = { args: { label: "Drinks / Coffee", source: "suggested" }, globals: { locale: "en" } };
export const LongNameMobile: Story = {
  args: { label: "Товары для дачи, рыбалки и прочих выходных за городом / Удочки, крючки, поплавки", source: "suggested" },
  globals: { viewport: { value: "phone", isRotated: false } }
};
