import { useId } from "react";
import { useTranslation } from "react-i18next";
import type { ProductCategory } from "../../api/types";
import { CategoryOptions } from "./CategoryOptions";
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
 * group itself is selectable too, as the first option inside its group
 * («Без подкатегории»).
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
        <CategoryOptions
          categories={categories}
          selected={value ?? ""}
          groupOptionText={t("categories.groupWithoutSubcategory")}
          selectedGroupText={(name) => name}
        />
      </select>
    </div>
  );
}
