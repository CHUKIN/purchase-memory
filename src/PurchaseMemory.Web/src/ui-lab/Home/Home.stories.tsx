import type { Meta, StoryObj } from "@storybook/react-vite";
import { DesktopHome } from "./DesktopHome";
import { MobileHome } from "./MobileHome";

const meta = { title: "Purchase Memory/Home", parameters: { layout: "fullscreen" } } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

export const MobileWarmJournal: Story = {
  render: () => <MobileHome />,
  globals: { viewport: { value: "phone", isRotated: false } }
};

export const DesktopWarmUtility: Story = {
  render: () => <DesktopHome />,
  globals: { viewport: { value: "desktop", isRotated: false } }
};
