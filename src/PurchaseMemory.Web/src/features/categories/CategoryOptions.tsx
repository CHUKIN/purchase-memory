import { useTranslation } from "react-i18next";
import type { ProductCategory } from "../../api/types";
import { categoryLabel, groupCategories } from "./categoryText";

export interface CategoryOptionsProps {
  categories: ProductCategory[];
  /** The select's current value, so the chosen option can carry its full path. */
  selected: string;
  /** Short text of the group's own option inside the open list. */
  groupOptionText: string;
  /**
   * Text of the group's own option once it is chosen: the filter says
   * «Овощи и фрукты — вся группа», the picker just «Овощи и фрукты».
   */
  selectedGroupText: (groupLabel: string) => string;
}

/**
 * The `<optgroup>` per top-level category for a native category select.
 *
 * Inside an open list the group name is already the optgroup heading, so the
 * options stay short («Вся группа», «Овощи», «Фрукты») instead of repeating
 * the group on every line — the iPhone wheel picker otherwise shows the group
 * name four or five times in a row. A closed select shows only the selected
 * option's text, though, and a bare «Прочее» there would be ambiguous, so the
 * selected option alone keeps its full path («Овощи и фрукты / Фрукты»).
 */
export function CategoryOptions({ categories, selected, groupOptionText, selectedGroupText }: CategoryOptionsProps) {
  const { t } = useTranslation();

  return (
    <>
      {groupCategories(categories).map(({ group, children }) => {
        const groupLabel = categoryLabel(t, group);
        return (
          <optgroup key={group.id} label={groupLabel}>
            <option value={group.id}>
              {group.id === selected ? selectedGroupText(groupLabel) : groupOptionText}
            </option>
            {children.map((child) => {
              const childLabel = categoryLabel(t, child);
              return (
                <option key={child.id} value={child.id}>
                  {child.id === selected ? `${groupLabel} / ${childLabel}` : childLabel}
                </option>
              );
            })}
          </optgroup>
        );
      })}
    </>
  );
}
