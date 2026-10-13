import type { Meta, StoryObj } from "@storybook/react-vite";
import { MemoryRouter } from "react-router";
import { Footnote, Page } from "../../design-system/components";
import { AppShell } from "./AppShell";

interface ShellStoryProps {
  path: string;
  title: string;
  reviewCount: number | null;
}

/** The tab bar with one empty screen above it; the badge on «Главная» counts the review queue. */
function ShellStory({ path, title, reviewCount }: ShellStoryProps) {
  return (
    <MemoryRouter initialEntries={[path]}>
      <AppShell reviewCount={reviewCount}>
        <Page title={title}>
          <Footnote>{path}</Footnote>
        </Page>
      </AppShell>
    </MemoryRouter>
  );
}

const meta = {
  title: "Purchase Memory/AppShell",
  component: ShellStory,
  parameters: { layout: "fullscreen" },
  args: { path: "/", title: "Главная", reviewCount: 4 }
} satisfies Meta<typeof ShellStory>;

export default meta;
type Story = StoryObj<typeof meta>;

export const HomeWithReviewBadge: Story = {
  globals: { viewport: { value: "phone", isRotated: false } }
};

export const PurchasesActive: Story = {
  args: { path: "/purchases", title: "Покупки", reviewCount: null },
  globals: { viewport: { value: "phone", isRotated: false } }
};

export const ProductDetailUnderProducts: Story = {
  args: { path: "/products/a", title: "Mleko Łaciate 3,2% 1L", reviewCount: null },
  globals: { viewport: { value: "phone", isRotated: false } }
};

export const DesktopWidth: Story = {
  args: { path: "/analytics", title: "Аналитика", reviewCount: 12 },
  globals: { viewport: { value: "desktop", isRotated: false } }
};
