import type { Meta, StoryObj } from "@storybook/react-vite";
import { ReceiptText } from "lucide-react";
import { useTranslation } from "react-i18next";
import { Button } from "../Button/Button";
import { EmptyState } from "./EmptyState";

const meta = {
  title: "Components/EmptyState",
  component: EmptyState,
  parameters: { layout: "centered" },
  args: { icon: <ReceiptText size={25} />, title: "Empty", description: "Nothing here yet" }
} satisfies Meta<typeof EmptyState>;
export default meta;
type Story = StoryObj<typeof meta>;

function LocalizedEmptyState() {
  const { t } = useTranslation();
  return <EmptyState icon={<ReceiptText size={25} />} title={t("empty.title")} description={t("empty.description")} action={<Button>{t("common.captureReceipt")}</Button>} />;
}

export const PurchaseHistory: Story = { render: () => <LocalizedEmptyState /> };
