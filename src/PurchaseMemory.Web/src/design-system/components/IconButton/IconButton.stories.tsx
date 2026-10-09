import type { Meta, StoryObj } from "@storybook/react-vite";
import { Pencil, Plus, Trash2, X } from "lucide-react";
import { useTranslation } from "react-i18next";
import { fn } from "storybook/test";
import { IconButton } from "./IconButton";
import styles from "../../stories/StoryShowcase.module.css";

const icons = { pencil: <Pencil />, trash: <Trash2 />, close: <X />, plus: <Plus /> };

const meta = {
  title: "Components/IconButton",
  component: IconButton,
  parameters: { layout: "centered" },
  argTypes: {
    intent: { control: "select", options: ["primary", "secondary", "quiet", "danger"] },
    iconOnly: { control: "inline-radio", options: ["always", "mobile"] },
    icon: { control: "select", options: Object.keys(icons), mapping: icons }
  },
  args: {
    label: "Изменить",
    icon: icons.pencil,
    intent: "quiet",
    iconOnly: "mobile",
    loading: false,
    disabled: false,
    onClick: fn()
  }
} satisfies Meta<typeof IconButton>;

export default meta;
type Story = StoryObj<typeof meta>;

/** Resize the canvas below 640 px (or pick the phone viewport) to see the text drop off. */
export const Playground: Story = {};

export const PhoneViewport: Story = {
  globals: { viewport: { value: "phone", isRotated: false } }
};

/** The real, translated labels used in the app; switch the locale toolbar between RU and EN. */
export const LocalizedActions: Story = {
  render: function LocalizedActions(args) {
    const { t } = useTranslation();
    return (
      <div className={styles.stack}>
        <div className={styles.row}>
          <IconButton {...args} label={t("categories.edit")} icon={<Pencil />} intent="secondary" iconOnly="mobile" />
          <IconButton {...args} label={t("tags.edit")} icon={<Pencil />} intent="secondary" iconOnly="mobile" />
          <IconButton {...args} label={t("impressions.delete")} icon={<Trash2 />} intent="danger" iconOnly="mobile" />
        </div>
        <div className={styles.row}>
          <IconButton {...args} label={t("purchaseEdit.removeLine", { number: 2 })} icon={<Trash2 />} intent="danger" iconOnly="always" />
          <IconButton {...args} label={`${t("tags.rename")}: акция`} text={t("tags.rename")} icon={<Pencil />} iconOnly="mobile" />
        </div>
      </div>
    );
  }
};

export const LocalizedActionsEnPhone: Story = {
  ...LocalizedActions,
  globals: { locale: "en", viewport: { value: "phone", isRotated: false } }
};

export const IntentAndStateMatrix: Story = {
  render: (args) => (
    <div className={styles.stack}>
      <div className={styles.row}>
        <IconButton {...args} label="Primary" icon={<Plus />} intent="primary" iconOnly="always" />
        <IconButton {...args} label="Secondary" icon={<Pencil />} intent="secondary" iconOnly="always" />
        <IconButton {...args} label="Quiet" icon={<X />} intent="quiet" iconOnly="always" />
        <IconButton {...args} label="Danger" icon={<Trash2 />} intent="danger" iconOnly="always" />
      </div>
      <div className={styles.row}>
        <IconButton {...args} label="Saving" icon={<Pencil />} loading />
        <IconButton {...args} label="Disabled" icon={<Trash2 />} intent="danger" disabled />
      </div>
    </div>
  )
};
