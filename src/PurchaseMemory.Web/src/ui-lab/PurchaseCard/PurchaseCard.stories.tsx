import type { Meta, StoryObj } from "@storybook/react-vite";
import { recentPurchases } from "../mockData";
import { PurchaseCard } from "./PurchaseCard";

const meta = {
  title: "Purchase Memory/PurchaseCard",
  component: PurchaseCard,
  parameters: { layout: "padded" },
  argTypes: { density: { control: "select", options: ["comfortable", "compact"] } },
  args: { purchase: recentPurchases[0], density: "comfortable", initiallyExpanded: false }
} satisfies Meta<typeof PurchaseCard>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Playground: Story = {};
export const Verified: Story = { args: { purchase: recentPurchases[0] } };
export const NeedsAttention: Story = { args: { purchase: recentPurchases[1], initiallyExpanded: true } };
export const PossibleDuplicate: Story = { args: { purchase: recentPurchases[2] } };
export const Compact: Story = { args: { purchase: recentPurchases[1], density: "compact" } };
