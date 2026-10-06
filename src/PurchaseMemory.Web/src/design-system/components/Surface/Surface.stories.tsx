import type { Meta, StoryObj } from "@storybook/react-vite";
import { Surface } from "./Surface";

const meta = {
  title: "Components/Surface",
  component: Surface,
  parameters: { layout: "centered" },
  argTypes: {
    tone: { control: "select", options: ["paper", "soft", "warm"] },
    density: { control: "select", options: ["comfortable", "compact"] }
  },
  args: { tone: "paper", density: "comfortable", elevated: false }
} satisfies Meta<typeof Surface>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Playground: Story = {
  render: (args) => <Surface {...args} style={{ width: 340 }}><strong>Purchase Memory surface</strong><p style={{ marginBottom: 0 }}>Tone, density and elevation are intentional layout choices.</p></Surface>
};
