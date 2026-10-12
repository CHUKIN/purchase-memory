import type { Meta, StoryObj } from "@storybook/react-vite";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { DateInput } from "./DateInput";
import showcase from "../../stories/StoryShowcase.module.css";
import styles from "./DateInput.stories.module.css";

/*
 * The «Любая дата» hint only appears in iOS/iPadOS WebKit (open Storybook in
 * Safari on an iPhone or the Simulator); desktop browsers draw their own hint.
 */
const meta = {
  title: "Components/DateInput",
  component: DateInput,
  parameters: { layout: "padded" },
  args: { value: "", type: "date" }
} satisfies Meta<typeof DateInput>;

export default meta;
type Story = StoryObj<typeof meta>;

function Controlled({ label, initial, type, emptyText }: { label: string; initial: string; type?: "date" | "datetime-local"; emptyText?: string }) {
  const [value, setValue] = useState(initial);
  return (
    <label className={styles.field}>
      <span className={styles.label}>{label}</span>
      <DateInput className={styles.input} type={type} value={value} emptyText={emptyText} onChange={(event) => setValue(event.target.value)} />
    </label>
  );
}

/** Two fields side by side, as in the feed filter; must fit a 320 px card. */
function Pair({ from, to }: { from: string; to: string }) {
  const { t } = useTranslation();
  return (
    <div className={`${showcase.narrow} ${styles.card}`}>
      <div className={styles.pair}>
        <Controlled label={t("feed.dateFrom")} initial={from} />
        <Controlled label={t("feed.dateTo")} initial={to} />
      </div>
    </div>
  );
}

export const Empty: Story = { render: () => <Pair from="" to="" /> };

export const Filled: Story = { render: () => <Pair from="2026-08-01" to="2026-10-12" /> };

function DateTimes() {
  const { t } = useTranslation();
  return (
    <div className={`${showcase.narrow} ${styles.card} ${showcase.stack}`}>
      <Controlled label={t("purchaseEdit.dateTime")} type="datetime-local" initial="2026-08-24T19:12" />
      <Controlled label={t("purchaseEdit.dateTime")} type="datetime-local" initial="" emptyText={t("purchaseEdit.unknownDate")} />
    </div>
  );
}

export const DateTimeLocal: Story = { render: () => <DateTimes /> };
