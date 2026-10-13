import { useTranslation } from "react-i18next";
import type { ProductCategory } from "../../api/types";
import { SelectRow } from "../../design-system/components";
import { categoryPath, categoryPickerGroups } from "./categoryText";

export interface CategoryPickerProps {
  categories: ProductCategory[];
  /** Selected category id; `null` is «Без категории». */
  value: string | null;
  onChange: (categoryId: string | null) => void;
  label?: string;
  disabled?: boolean;
}

/**
 * A product's category as a list row that opens the tree: one section per
 * group, the group itself first («Без подкатегории»), then its
 * subcategories, with a checkmark on the current one.
 */
export function CategoryPicker({ categories, value, onChange, label, disabled = false }: CategoryPickerProps) {
  const { t } = useTranslation();
  return (
    <SelectRow
      label={label ?? t("categories.pickerLabel")}
      valueText={categoryPath(t, categories, value) ?? t("categories.none")}
      value={value ?? ""}
      disabled={disabled}
      searchable
      onChange={(next) => onChange(next === "" ? null : next)}
      groups={categoryPickerGroups(t, categories, [{ value: "", label: t("categories.none") }], t("categories.groupWithoutSubcategory"))}
    />
  );
}
