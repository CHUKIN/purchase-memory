import { Sparkles, Tag } from "lucide-react";
import { useTranslation } from "react-i18next";
import type { AttributeSource } from "../../api/types";
import styles from "./CategoryChip.module.css";

export interface CategoryChipProps {
  /** Already translated label or path; `null` shows «Без категории». */
  label: string | null;
  source?: AttributeSource | null;
}

/**
 * The product's category at a glance. A suggestion says so in words, not only
 * with the dashed outline, so it reads the same to a screen reader and in
 * grayscale.
 */
export function CategoryChip({ label, source = null }: CategoryChipProps) {
  const { t } = useTranslation();
  const suggested = label !== null && source === "suggested";
  const Icon = suggested ? Sparkles : Tag;

  return (
    <span
      className={styles.chip}
      data-state={label === null ? "none" : suggested ? "suggested" : "confirmed"}
      // Long names are cut after two lines; the tooltip keeps the whole path.
      title={suggested ? `${label} — ${t("categories.suggestedHint")}` : label ?? undefined}
    >
      <Icon aria-hidden="true" size={13} strokeWidth={2.25} />
      <span className={styles.label}>{label ?? t("categories.none")}</span>
      {suggested && <span className={styles.marker}>{t("categories.suggested")}</span>}
    </span>
  );
}
