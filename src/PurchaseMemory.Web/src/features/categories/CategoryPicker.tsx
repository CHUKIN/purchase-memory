import { useId } from "react";
import { useTranslation } from "react-i18next";
import type { ProductCategory } from "../../api/types";
import { categoryLabel, groupCategories } from "./categoryText";
import styles from "./CategoryPicker.module.css";

export interface CategoryPickerProps {
  categories: ProductCategory[];
  /** Selected category id; `null` is «Без категории». */
  value: string | null;
  onChange: (categoryId: string | null) => void;
  label?: string;
  disabled?: boolean;
}

/**
 * A native select with one option group per top-level category. It is the
 * most robust accessible tree picker available: keyboard, screen readers and
 * the phone's own wheel picker all understand it without custom ARIA. The
 * group itself is selectable too, as the first option inside its group.
 */
export function CategoryPicker({ categories, value, onChange, label, disabled = false }: CategoryPickerProps) {
  const { t } = useTranslation();
  const id = useId();

  return (
    <div className={styles.field}>
      <label className={styles.label} htmlFor={id}>{label ?? t("categories.pickerLabel")}</label>
      <select
        id={id}
        className={styles.select}
        value={value ?? ""}
        disabled={disabled}
        onChange={(event) => onChange(event.target.value === "" ? null : event.target.value)}
      >
        <option value="">{t("categories.none")}</option>
        {groupCategories(categories).map(({ group, children }) => {
          const groupLabel = categoryLabel(t, group);
          return (
            <optgroup key={group.id} label={groupLabel}>
              <option value={group.id}>{groupLabel}</option>
              {children.map((child) => (
                <option key={child.id} value={child.id}>{`${groupLabel} / ${categoryLabel(t, child)}`}</option>
              ))}
            </optgroup>
          );
        })}
      </select>
    </div>
  );
}
