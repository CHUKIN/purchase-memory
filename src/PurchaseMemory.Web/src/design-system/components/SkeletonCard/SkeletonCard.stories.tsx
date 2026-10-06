import type { Meta, StoryObj } from "@storybook/react-vite";
import { SkeletonCard } from "./SkeletonCard";
import styles from "../../stories/StoryShowcase.module.css";

const meta = {
  title: "Components/SkeletonCard",
  component: SkeletonCard,
  parameters: { layout: "padded" }
} satisfies Meta<typeof SkeletonCard>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Playground: Story = {};
export const List: Story = {
  render: () => (
    <div className={`${styles.stack} ${styles.narrow}`}>
      <SkeletonCard />
      <SkeletonCard />
      <SkeletonCard />
    </div>
  )
};
