import type { Meta, StoryObj } from "@storybook/react-vite";
import { Store } from "lucide-react";
import { useTranslation } from "react-i18next";
import { Field } from "./Field";
import styles from "../../stories/StoryShowcase.module.css";

const meta = {
  title: "Components/Field",
  component: Field,
  parameters: { layout: "centered" },
  args: { required: false, disabled: false }
} satisfies Meta<typeof Field>;

export default meta;
type Story = StoryObj<typeof meta>;

function LocalizedField(props: { error?: boolean; disabled?: boolean; required?: boolean }) {
  const { t } = useTranslation();
  return <div className={styles.narrow}><Field {...props} leftSection={<Store aria-hidden="true" size={17} />} label={t("field.exampleLabel")} description={t("field.exampleDescription")} placeholder={t("field.examplePlaceholder")} error={props.error ? t("field.exampleError") : undefined} /></div>;
}

export const Playground: Story = {
  render: (args) => <LocalizedField error={Boolean(args.error)} disabled={args.disabled} required={args.required} />,
  argTypes: { error: { control: "boolean" } }
};

export const States: Story = {
  render: () => <div className={`${styles.stack} ${styles.narrow}`}><LocalizedField /><LocalizedField required /><LocalizedField error /><LocalizedField disabled /></div>
};
