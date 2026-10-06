import type { Meta, StoryObj } from "@storybook/react-vite";
import { StatusBadge, type PurchaseStatus } from "./StatusBadge";
import styles from "../../stories/StoryShowcase.module.css";

const statuses: PurchaseStatus[] = ["uploaded", "processing", "ready", "needsAttention", "verified", "possibleDuplicate", "failed"];

const meta = {
  title: "Components/StatusBadge",
  component: StatusBadge,
  parameters: { layout: "centered" },
  argTypes: { status: { control: "select", options: statuses } },
  args: { status: "needsAttention" }
} satisfies Meta<typeof StatusBadge>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Playground: Story = {};
export const AllStatuses: Story = { render: () => <div className={styles.row}>{statuses.map((status) => <StatusBadge status={status} key={status} />)}</div> };
