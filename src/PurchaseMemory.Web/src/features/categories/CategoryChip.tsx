import { Sparkles, Tag } from "lucide-react";
import { useTranslation } from "react-i18next";
import type { AttributeSource } from "../../api/types";

export interface CategoryChipProps {
  /** Already translated label or path; `null` shows «Без категории». */
  label: string | null;
  source?: AttributeSource | null;
}

/**
 * The product's category at a glance. A suggestion says so in words, not only
 * with the warm colour, so it reads the same to a screen reader and in
 * grayscale.
 */
export function CategoryChip({ label, source = null }: CategoryChipProps) {
  const { t } = useTranslation();
  const suggested = label !== null && source === "suggested";
  const Icon = suggested ? Sparkles : Tag;

  return (
    <span
      className={["pm-chip", label === null ? "pm-none" : suggested ? "pm-suggested" : ""].filter(Boolean).join(" ")}
      title={suggested ? `${label} — ${t("categories.suggestedHint")}` : label ?? undefined}
    >
      <Icon aria-hidden="true" size={12} strokeWidth={2.25} />
      <span>{label ?? t("categories.none")}</span>
      {suggested && <span className="pm-chip-marker">· {t("categories.suggested")}</span>}
    </span>
  );
}
