import type { Meta, StoryObj } from "@storybook/react-vite";
import { Camera } from "lucide-react";
import { Button } from "./Button";
import styles from "../../stories/StoryShowcase.module.css";

const meta = {
  title: "Components/Button",
  component: Button,
  parameters: { layout: "centered" },
  argTypes: {
    intent: { control: "select", options: ["primary", "secondary", "quiet", "danger"] }
  },
  args: {
    children: "Photograph a receipt",
    intent: "primary",
    loading: false,
    disabled: false
  }
} satisfies Meta<typeof Button>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Playground: Story = {};

export const WithIcon: Story = {
  args: { leftSection: <Camera aria-hidden="true" size={18} /> }
};

export const IntentAndStateMatrix: Story = {
  render: () => (
    <div className={styles.stack}>
      <div className={styles.row}><Button>Primary</Button><Button intent="secondary">Secondary</Button><Button intent="quiet">Quiet</Button><Button intent="danger">Remove</Button></div>
      <div className={styles.row}><Button loading>Saving</Button><Button disabled>Disabled</Button></div>
    </div>
  )
};
