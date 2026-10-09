import { Pencil, Plus, Trash2 } from "lucide-react";
import { useId, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import type { ProductCategory } from "../../api/types";
import { Button, Field, IconButton } from "../../design-system/components";
import { categoryLabel, groupCategories } from "./categoryText";
import styles from "./CategoryManager.module.css";

export interface CategoryManagerProps {
  categories: ProductCategory[];
  onCreate: (name: string, parentId: string | null) => Promise<unknown>;
  onRename: (categoryId: string, name: string) => Promise<unknown>;
  onDelete: (categoryId: string) => Promise<unknown>;
  /** Open by default; the page keeps it collapsed. */
  defaultOpen?: boolean;
}

/**
 * Where the household edits its tree. Kept deliberately plain — a list with
 * inline rename and a two-step delete — because it is used rarely and must
 * never move products by accident.
 */
export function CategoryManager({ categories, onCreate, onRename, onDelete, defaultOpen = false }: CategoryManagerProps) {
  const { t } = useTranslation();
  const formId = useId();
  const [name, setName] = useState("");
  const [parentId, setParentId] = useState<string>("");
  const [renaming, setRenaming] = useState<{ id: string; name: string } | null>(null);
  const [confirmingDelete, setConfirmingDelete] = useState<string | null>(null);
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

  async function create(event: FormEvent) {
    event.preventDefault();
    if (name.trim() === "") return;
    if (await run(() => onCreate(name.trim(), parentId === "" ? null : parentId))) setName("");
  }

  async function rename(event: FormEvent) {
    event.preventDefault();
    if (!renaming || renaming.name.trim() === "") return;
    if (await run(() => onRename(renaming.id, renaming.name.trim()))) setRenaming(null);
  }

  function row(category: ProductCategory, hasChildren: boolean) {
    const label = categoryLabel(t, category);
    if (renaming?.id === category.id) {
      return (
        <form className={styles.row} onSubmit={rename}>
          <Field
            aria-label={t("categories.rename")}
            value={renaming.name}
            maxLength={100}
            autoFocus
            onChange={(event) => setRenaming({ id: category.id, name: event.currentTarget.value })}
          />
          <div className={styles.rowActions}>
            <Button type="submit" loading={busy}>{t("categories.save")}</Button>
            <Button intent="quiet" onClick={() => setRenaming(null)} disabled={busy}>{t("categories.cancel")}</Button>
          </div>
        </form>
      );
    }

    return (
      <div className={styles.row}>
        <span className={styles.name}>
          {label}
          <span className={styles.count}>{t("categories.productCount", { count: category.productCount })}</span>
        </span>
        <div className={styles.rowActions}>
          {confirmingDelete === category.id
            ? (
              <>
                <span className={styles.confirm}>{t("categories.deleteConfirm", { name: label })}</span>
                <Button intent="danger" loading={busy} onClick={() => void run(() => onDelete(category.id)).then((done) => done && setConfirmingDelete(null))}>
                  {t("categories.deleteYes")}
                </Button>
                <Button intent="quiet" onClick={() => setConfirmingDelete(null)} disabled={busy}>{t("categories.cancel")}</Button>
              </>
            )
            : (
              <>
                <IconButton iconOnly="mobile" icon={<Pencil />} label={`${t("categories.rename")}: ${label}`} text={t("categories.rename")}
                  onClick={() => setRenaming({ id: category.id, name: label })} disabled={busy} />
                <IconButton intent="danger" iconOnly="mobile" icon={<Trash2 />} label={`${t("categories.delete")}: ${label}`} text={t("categories.delete")}
                  onClick={() => setConfirmingDelete(category.id)} disabled={busy || hasChildren}
                  title={hasChildren ? t("categories.deleteHasChildren") : undefined} />
              </>
            )}
        </div>
      </div>
    );
  }

  return (
    <details className={styles.manager} open={defaultOpen}>
      <summary>{t("categories.manageTitle")}</summary>
      <p className={styles.intro}>{t("categories.manageIntro")}</p>

      <form className={styles.create} onSubmit={create} aria-labelledby={`${formId}-title`}>
        <span id={`${formId}-title`} className="visually-hidden">{t("categories.add")}</span>
        <Field label={t("categories.newName")} value={name} maxLength={100} onChange={(event) => setName(event.currentTarget.value)} disabled={busy} />
        <label className={styles.parent}>
          <span>{t("categories.newParent")}</span>
          <select value={parentId} onChange={(event) => setParentId(event.target.value)} disabled={busy}>
            <option value="">{t("categories.topLevel")}</option>
            {groups.map(({ group }) => <option key={group.id} value={group.id}>{categoryLabel(t, group)}</option>)}
          </select>
        </label>
        <Button type="submit" loading={busy} disabled={name.trim() === ""} leftSection={<Plus aria-hidden="true" size={16} />}>
          {t("categories.add")}
        </Button>
      </form>

      {error && <p className={styles.error} role="alert">{error}</p>}

      <ul className={styles.tree} aria-label={t("categories.treeLabel")}>
        {groups.map(({ group, children }) => (
          <li key={group.id}>
            {row(group, children.length > 0)}
            {children.length > 0 && (
              <ul className={styles.children}>
                {children.map((child) => <li key={child.id}>{row(child, false)}</li>)}
              </ul>
            )}
          </li>
        ))}
      </ul>
    </details>
  );
}
