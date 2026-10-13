import { IonAlert, IonItem, IonLabel, IonList, IonNote } from "@ionic/react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import type { AttributeSource, ProductCategory } from "../../api/types";
import { Footnote, SectionHeader } from "../../design-system/components";
import { CategoryPicker } from "./CategoryPicker";

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
}

/**
 * Category and brand of one product as two rows. Picking a category or
 * entering a brand saves it at once; a suggestion from the AI can be
 * confirmed as it is. A human choice here is authoritative: it covers every
 * purchase of the product and AI never overwrites it.
 */
export function ProductClassification({ categories, categoryId, categorySource, brand, brandSource, onSave }: ProductClassificationProps) {
  const { t } = useTranslation();
  const [saving, setSaving] = useState(false);
  const [failed, setFailed] = useState(false);
  const [editingBrand, setEditingBrand] = useState(false);
  const suggestedSomething = categorySource === "suggested" || brandSource === "suggested";

  async function save(change: ClassificationChange) {
    setSaving(true);
    setFailed(false);
    try {
      await onSave(change);
    } catch {
      setFailed(true);
    } finally {
      setSaving(false);
    }
  }

  return (
    <section aria-labelledby="classification-title">
      <SectionHeader id="classification-title">{t("categories.title")}</SectionHeader>
      <IonList inset className="pm-after-header">
        <CategoryPicker
          categories={categories}
          value={categoryId}
          label={categorySource === "suggested" ? `${t("categories.category")} · ${t("categories.suggested")}` : t("categories.category")}
          disabled={saving}
          onChange={(next) => void save({ categoryId: next })}
        />
        <IonItem button detail disabled={saving} lines={suggestedSomething ? undefined : "none"} onClick={() => setEditingBrand(true)}>
          <IonLabel>{t("categories.brand")}</IonLabel>
          <IonNote slot="end" className="pm-pick-value">
            {brand ?? t("categories.noBrand")}
            {brand && brandSource === "suggested" && ` · ${t("categories.suggested")}`}
          </IonNote>
        </IonItem>
        {suggestedSomething && (
          <IonItem
            button
            detail={false}
            lines="none"
            disabled={saving}
            onClick={() => void save({
              ...(categorySource === "suggested" ? { categoryId } : {}),
              ...(brandSource === "suggested" ? { brand } : {})
            })}
          >
            <IonLabel color="primary">{t("categories.confirm")}</IonLabel>
          </IonItem>
        )}
      </IonList>
      {suggestedSomething && <Footnote>{t("categories.suggestedHint")}</Footnote>}
      <Footnote>{t("categories.appliesToAll")}</Footnote>
      {failed && <Footnote tone="danger" role="alert">{t("categories.saveError")}</Footnote>}

      <IonAlert
        isOpen={editingBrand}
        header={t("categories.brand")}
        inputs={[{ name: "brand", type: "text", value: brand ?? "", placeholder: t("categories.brandPlaceholder"), attributes: { maxlength: 100 } }]}
        buttons={[
          { text: t("categories.cancel"), role: "cancel" },
          {
            text: t("categories.save"),
            handler: (values: { brand?: string }) => {
              const next = (values.brand ?? "").trim();
              void save({ brand: next === "" ? null : next });
            }
          }
        ]}
        onDidDismiss={() => setEditingBrand(false)}
      />
    </section>
  );
}
