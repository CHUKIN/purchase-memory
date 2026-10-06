import { Pencil, Plus, Trash2 } from "lucide-react";
import { useId, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { ApiError } from "../../api/client";
import type { TagSummary } from "../../api/types";
import { Button, Field } from "../../design-system/components";
// Same look as the category manager right above it on the page.
import styles from "../categories/CategoryManager.module.css";
import { maximumTagNameLength } from "./ProductTags";
import tagStyles from "./Tags.module.css";

export interface TagManagerProps {
  tags: TagSummary[];
  onCreate: (name: string) => Promise<unknown>;
  onRename: (tagId: string, name: string) => Promise<unknown>;
  onDelete: (tagId: string) => Promise<unknown>;
  /** Open by default; the page keeps it collapsed. */
  defaultOpen?: boolean;
}

/**
 * Where the household renames and deletes its tags. A deleted tag simply
 * disappears from its products; the products themselves stay.
 */
export function TagManager({ tags, onCreate, onRename, onDelete, defaultOpen = false }: TagManagerProps) {
  const { t } = useTranslation();
  const formId = useId();
  const [name, setName] = useState("");
  const [renaming, setRenaming] = useState<{ id: string; name: string } | null>(null);
  const [confirmingDelete, setConfirmingDelete] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function run(action: () => Promise<unknown>) {
    setBusy(true);
    setError(null);
    try {
      await action();
      return true;
    } catch (failure) {
      setError(failure instanceof ApiError && failure.status === 409
        ? t("tags.duplicate")
        : t("tags.actionError", { message: failure instanceof Error ? failure.message : String(failure) }));
      return false;
    } finally {
      setBusy(false);
    }
  }

  async function create(event: FormEvent) {
    event.preventDefault();
    if (name.trim() === "") return;
    if (await run(() => onCreate(name.trim()))) setName("");
  }

  async function rename(event: FormEvent) {
    event.preventDefault();
    if (!renaming || renaming.name.trim() === "") return;
    if (await run(() => onRename(renaming.id, renaming.name.trim()))) setRenaming(null);
  }

  function row(tag: TagSummary) {
    if (renaming?.id === tag.id) {
      return (
        <form className={styles.row} onSubmit={rename}>
          <Field
            aria-label={t("tags.rename")}
            value={renaming.name}
            maxLength={maximumTagNameLength}
            autoFocus
            onChange={(event) => setRenaming({ id: tag.id, name: event.currentTarget.value })}
          />
          <div className={styles.rowActions}>
            <Button type="submit" loading={busy}>{t("tags.save")}</Button>
            <Button intent="quiet" onClick={() => setRenaming(null)} disabled={busy}>{t("tags.cancel")}</Button>
          </div>
        </form>
      );
    }

    return (
      <div className={styles.row}>
        <span className={styles.name}>
          {tag.name}
          <span className={styles.count}>{t("tags.productCount", { count: tag.productCount })}</span>
        </span>
        <div className={styles.rowActions}>
          {confirmingDelete === tag.id
            ? (
              <>
                <span className={styles.confirm}>{t("tags.deleteConfirm", { name: tag.name })}</span>
                <Button intent="danger" loading={busy} onClick={() => void run(() => onDelete(tag.id)).then((done) => done && setConfirmingDelete(null))}>
                  {t("tags.deleteYes")}
                </Button>
                <Button intent="quiet" onClick={() => setConfirmingDelete(null)} disabled={busy}>{t("tags.cancel")}</Button>
              </>
            )
            : (
              <>
                <Button intent="quiet" aria-label={`${t("tags.rename")}: ${tag.name}`} leftSection={<Pencil aria-hidden="true" size={15} />}
                  onClick={() => setRenaming({ id: tag.id, name: tag.name })} disabled={busy}>
                  <span className={styles.actionText}>{t("tags.rename")}</span>
                </Button>
                <Button intent="quiet" aria-label={`${t("tags.delete")}: ${tag.name}`} leftSection={<Trash2 aria-hidden="true" size={15} />}
                  onClick={() => setConfirmingDelete(tag.id)} disabled={busy}>
                  <span className={styles.actionText}>{t("tags.delete")}</span>
                </Button>
              </>
            )}
        </div>
      </div>
    );
  }

  return (
    <details className={styles.manager} open={defaultOpen}>
      <summary>{t("tags.manageTitle")}</summary>
      <p className={styles.intro}>{t("tags.manageIntro")}</p>

      <form className={`${styles.create} ${tagStyles.twoColumns}`} onSubmit={create} aria-labelledby={`${formId}-title`}>
        <span id={`${formId}-title`} className="visually-hidden">{t("tags.add")}</span>
        <Field label={t("tags.newLabel")} placeholder={t("tags.newPlaceholder")} value={name} maxLength={maximumTagNameLength}
          onChange={(event) => setName(event.currentTarget.value)} disabled={busy} />
        <Button type="submit" loading={busy} disabled={name.trim() === ""} leftSection={<Plus aria-hidden="true" size={16} />}>
          {t("tags.add")}
        </Button>
      </form>

      {error && <p className={styles.error} role="alert">{error}</p>}

      {tags.length === 0
        ? <p className={styles.intro}>{t("tags.noTagsYet")}</p>
        : (
          <ul className={styles.tree} aria-label={t("tags.listLabel")}>
            {tags.map((tag) => <li key={tag.id}>{row(tag)}</li>)}
          </ul>
        )}
    </details>
  );
}
