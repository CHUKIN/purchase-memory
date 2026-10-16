import { CircleAlert } from "lucide-react";
import { useTranslation } from "react-i18next";

/**
 * The «проверить» mark of a product the matcher was unsure about. Words, not
 * only the warm colour, so it reads the same to a screen reader and in grayscale.
 */
export function ReviewFlagChip() {
  const { t } = useTranslation();
  return (
    <span className="pm-chip pm-suggested" title={t("products.needsReviewHint")}>
      <CircleAlert aria-hidden="true" size={12} strokeWidth={2.25} />
      <span>{t("products.needsReviewBadge")}</span>
    </span>
  );
}
