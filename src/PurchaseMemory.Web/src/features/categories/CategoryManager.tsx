import { IonInput, IonItem, IonLabel, IonList } from "@ionic/react";
import { Plus } from "lucide-react";
import { Fragment, useState } from "react";
import { useTranslation } from "react-i18next";
import type { ProductCategory } from "../../api/types";
import { Footnote, SectionHeader, SelectRow } from "../../design-system/components";
import { categoryLabel, groupCategories } from "./categoryText";
import { ManagedRow } from "./ManagedRow";

export interface CategoryManagerProps {
  categories: ProductCategory[];
  onCreate: (name: string, parentId: string | null) => Promise<unknown>;
  onRename: (categoryId: string, name: string) => Promise<unknown>;
  onDelete: (categoryId: string) => Promise<unknown>;
}

/**
 * Where the household edits its tree. Kept deliberately plain — a form to add
 * one category, then the tree with a tap for «Переименовать» or «Удалить» —
 * because it is used rarely and must never move products by accident.
 */
export function CategoryManager({ categories, onCreate, onRename, onDelete }: CategoryManagerProps) {
  const { t } = useTranslation();
  const [name, setName] = useState("");
  const [parentId, setParentId] = useState<string>("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const groups = groupCategories(categories);

  async function run(action: () => Promise<unknown>) {
    setBusy(true);
    setError(null);
    try {
      await action();
      return true;
    } catch (failure) {
      setError(t("categories.actionError", { message: failure instanceof Error ? failure.message : String(failure) }));
      return false;
    } finally {
      setBusy(false);
    }
  }

  async function create() {
    if (name.trim() === "") return;
    if (await run(() => onCreate(name.trim(), parentId === "" ? null : parentId))) setName("");
  }

  const text = (label: string, hasChildren: boolean) => ({
    rename: t("categories.rename"),
    delete: t("categories.delete"),
    cancel: t("categories.cancel"),
    save: t("categories.save"),
    deleteConfirm: t("categories.deleteConfirm", { name: label }),
    deleteYes: t("categories.deleteYes"),
    deleteBlocked: hasChildren ? t("categories.deleteHasChildren") : undefined
  });

  return (
    <>
      <Footnote>{t("categories.manageIntro")}</Footnote>

      <SectionHeader level={3}>{t("categories.add")}</SectionHeader>
      <IonList inset className="pm-after-header">
        <IonItem>
          <IonInput label={t("categories.newName")} labelPlacement="stacked" value={name} maxlength={100} disabled={busy} onIonInput={(event) => setName(String(event.detail.value ?? ""))} />
        </IonItem>
        <SelectRow
          label={t("categories.newParent")}
          valueText={parentId === "" ? t("categories.topLevel") : categoryLabel(t, categories.find((entry) => entry.id === parentId) ?? { name: "", systemKey: null })}
          value={parentId}
          onChange={setParentId}
          disabled={busy}
          groups={[{ options: [{ value: "", label: t("categories.topLevel") }, ...groups.map(({ group }) => ({ value: group.id, label: categoryLabel(t, group) }))] }]}
        />
        <IonItem button detail={false} lines="none" disabled={busy || name.trim() === ""} onClick={() => void create()}>
          <Plus slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
          <IonLabel color="primary">{t("categories.add")}</IonLabel>
        </IonItem>
      </IonList>

      {error && <Footnote tone="danger" role="alert">{error}</Footnote>}

      <div role="list" aria-label={t("categories.treeLabel")}>
        {groups.map(({ group, children }) => {
          const label = categoryLabel(t, group);
          return (
            <Fragment key={group.id}>
              <IonList inset role="listitem">
                <ManagedRow
                  label={label}
                  detail={t("categories.productCount", { count: group.productCount })}
                  strong
                  disabled={busy}
                  maxLength={100}
                  text={text(label, children.length > 0)}
                  last={children.length === 0}
                  onRename={(next) => void run(() => onRename(group.id, next))}
                  onDelete={() => void run(() => onDelete(group.id))}
                />
                {children.map((child, index) => {
                  const childLabel = categoryLabel(t, child);
                  return (
                    <ManagedRow
                      key={child.id}
                      label={childLabel}
                      detail={t("categories.productCount", { count: child.productCount })}
                      nested
                      disabled={busy}
                      maxLength={100}
                      text={text(childLabel, false)}
                      last={index === children.length - 1}
                      onRename={(next) => void run(() => onRename(child.id, next))}
                      onDelete={() => void run(() => onDelete(child.id))}
                    />
                  );
                })}
              </IonList>
            </Fragment>
          );
        })}
      </div>
    </>
  );
}
