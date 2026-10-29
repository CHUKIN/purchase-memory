import type { Meta, StoryObj } from "@storybook/react-vite";
import { MemoryRouter } from "react-router";
import { HomeReviewCard } from "./HomeReviewCard";

const phone = { viewport: { value: "phone", isRotated: false } };

const meta = {
  title: "Purchase Memory/Home/Review card",
  component: HomeReviewCard,
  parameters: { layout: "fullscreen" },
  decorators: [(Story) => <MemoryRouter><div style={{ padding: "12px 0", background: "#f2f2f7" }}><Story /></div></MemoryRouter>],
  globals: phone,
  args: { count: 12 }
} satisfies Meta<typeof HomeReviewCard>;

export default meta;
type Story = StoryObj<typeof meta>;

/** Something waits: warm card, ⚠ and the same number as the tab badge. */
export const PendingMobile: Story = {};

export const PendingMobileEn: Story = { globals: { ...phone, locale: "en" } };

/** Like the tab badge, a long list shows 99+. */
export const ManyMobile: Story = { args: { count: 140 } };

/** Nothing waits: «Всё проверено», no count. */
export const AllCheckedMobile: Story = { args: { count: 0 } };

/** The count is not known yet (first load or the request failed). */
export const UnknownMobile: Story = { args: { count: null } };
