import { Hash } from "lucide-react";
import { useTranslation } from "react-i18next";
import type { ProductTag } from "../../api/types";
import styles from "./Tags.module.css";

export interface TagChipsProps {
  tags: ProductTag[];
  /** Says «Тегов нет» instead of rendering nothing; the catalog stays quiet. */
  showEmpty?: boolean;
}

/**
 * A product's tags as small chips. The «#» marks them as tags rather than the
 * one category, which uses the tag-shaped icon.
 */
export function TagChips({ tags, showEmpty = false }: TagChipsProps) {
  const { t } = useTranslation();
  if (tags.length === 0) {
    return showEmpty ? <span className={styles.empty}>{t("tags.none")}</span> : null;
  }

  return (
    <ul className={styles.chips} aria-label={t("tags.listOnProduct")}>
      {tags.map((tag) => (
        <li key={tag.id} className={styles.chip} title={tag.name}>
          <Hash aria-hidden="true" size={12} strokeWidth={2.5} />
          <span className={styles.chipLabel}>{tag.name}</span>
        </li>
      ))}
    </ul>
  );
}
