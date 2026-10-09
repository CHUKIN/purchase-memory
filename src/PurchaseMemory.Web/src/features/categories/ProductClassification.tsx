import { Pencil } from "lucide-react";
import { useId, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import type { AttributeSource, ProductCategory } from "../../api/types";
import { Button, Field, IconButton, Surface } from "../../design-system/components";
import { CategoryChip } from "./CategoryChip";
import { CategoryPicker } from "./CategoryPicker";
import { categoryPath } from "./categoryText";
import styles from "./ProductClassification.module.css";

export interface ClassificationChange {
  categoryId?: string | null;
  brand?: string | null;
}

export interface ProductClassificationProps {
  categories: ProductCategory[];
  categoryId: string | null;
  categorySource: AttributeSource | null;
  brand: string | null;
  brandSource: AttributeSource | null;
  /** Everything sent becomes the person's confirmed decision. */
  onSave: (change: ClassificationChange) => Promise<unknown>;
  /** Starts in edit mode; for stories and tests. */
  initiallyEditing?: boolean;
}

/**
 * Category and brand of one product. A human choice here is authoritative:
 * it covers every purchase of the product and AI never overwrites it.
 */
export function ProductClassification({
  categories,
  categoryId,
  categorySource,
  brand,
  brandSource,
  onSave,
  initiallyEditing = false
}: ProductClassificationProps) {
  const { t } = useTranslation();
  const titleId = useId();
  const [editing, setEditing] = useState(initiallyEditing);
  const [draftCategory, setDraftCategory] = useState(categoryId);
  const [draftBrand, setDraftBrand] = useState(brand ?? "");
  const [saving, setSaving] = useState(false);
  const [failed, setFailed] = useState(false);

  const label = categoryPath(t, categories, categoryId);
  const suggestedSomething = categorySource === "suggested" || brandSource === "suggested";

  async function save(change: ClassificationChange) {
    setSaving(true);
    setFailed(false);
    try {
      await onSave(change);
      setEditing(false);
    } catch {
      setFailed(true);
    } finally {
      setSaving(false);
    }
  }

  function startEditing() {
    setDraftCategory(categoryId);
    setDraftBrand(brand ?? "");
    setFailed(false);
    setEditing(true);
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    void save({ categoryId: draftCategory, brand: draftBrand.trim() === "" ? null : draftBrand.trim() });
  }

  return (
    <Surface as="section" className={styles.section} aria-labelledby={titleId}>
      <div className={styles.heading}>
        <h2 id={titleId}>{t("categories.title")}</h2>
        {!editing && (
          <IconButton intent="secondary" iconOnly="mobile" icon={<Pencil />} label={t("categories.edit")} onClick={startEditing} disabled={saving} />
        )}
      </div>

      {!editing && (
        <>
          <dl className={styles.values}>
            <div>
              <dt>{t("categories.category")}</dt>
              <dd><CategoryChip label={label} source={categorySource} /></dd>
            </div>
            <div>
              <dt>{t("categories.brand")}</dt>
              <dd className={brand ? undefined : styles.muted}>
                {brand ?? t("categories.noBrand")}
                {brand && brandSource === "suggested" && <span className={styles.marker}> · {t("categories.suggested")}</span>}
              </dd>
            </div>
          </dl>
          {suggestedSomething && (
            <>
              <p className={styles.hint}>{t("categories.suggestedHint")}</p>
              <div className={styles.actions}>
                <Button
                  loading={saving}
                  onClick={() => void save({
                    ...(categorySource === "suggested" ? { categoryId } : {}),
                    ...(brandSource === "suggested" ? { brand } : {})
                  })}
                >
                  {t("categories.confirm")}
                </Button>
              </div>
            </>
          )}
        </>
      )}

      {editing && (
        <form className={styles.form} onSubmit={submit}>
          <CategoryPicker categories={categories} value={draftCategory} onChange={setDraftCategory} disabled={saving} />
          <Field
            label={t("categories.brand")}
            placeholder={t("categories.brandPlaceholder")}
            value={draftBrand}
            maxLength={100}
            disabled={saving}
            onChange={(event) => setDraftBrand(event.currentTarget.value)}
          />
          <p className={styles.hint}>{t("categories.appliesToAll")}</p>
          <div className={styles.actions}>
            <Button type="submit" loading={saving}>{t("categories.save")}</Button>
            <Button intent="quiet" onClick={() => setEditing(false)} disabled={saving}>{t("categories.cancel")}</Button>
          </div>
        </form>
      )}

      {failed && <p className={styles.error} role="alert">{t("categories.saveError")}</p>}
    </Surface>
  );
}
