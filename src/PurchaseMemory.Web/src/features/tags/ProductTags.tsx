import { Check, Pencil, Plus } from "lucide-react";
import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { ApiError } from "../../api/client";
import type { ProductTag, TagSuggestionsResponse, TagSummary } from "../../api/types";
import { Button, Field, IconButton, Surface } from "../../design-system/components";
import { HelpLink } from "../help/HelpLink";
import { TagChips } from "./TagChips";
import { TagSuggestions } from "./TagSuggestions";
import { tagKey } from "./tagKey";
import styles from "./Tags.module.css";

/** Mirrors `ProductTagLink.MaximumTagsPerProduct` and `ProductTag.MaximumNameLength` on the server. */
export const maximumTagsPerProduct = 20;
export const maximumTagNameLength = 40;

export interface ProductTagsProps {
  tags: ProductTag[];
  /** Every household tag, to pick from. */
  allTags: TagSummary[];
  /** Replaces the product's tags with exactly these ids. */
  onSave: (tagIds: string[]) => Promise<unknown>;
  /** Creates a household tag and returns it, so it can be picked at once. */
  onCreate: (name: string) => Promise<ProductTag>;
  /** Asks the AI which existing tags fit; with it the section offers «Предложить теги». */
  onSuggest?: () => Promise<TagSuggestionsResponse>;
  /**
   * The product's tags at the moment of accepting (the query cache), so an
   * accept never builds on a stale render; the `tags` prop is the fallback.
   */
  readCurrentTagIds?: () => string[] | undefined;
  /** Starts in edit mode; for stories and tests. */
  initiallyEditing?: boolean;
}

/**
 * The product's tags: extra, overlapping slices next to the one category.
 * Editing shows every household tag as a toggle (a real button with
 * `aria-pressed`, so keyboard and screen readers need nothing custom) and a
 * field to create a new tag, which is picked straight away.
 */
export function ProductTags({ tags, allTags, onSave, onCreate, onSuggest, readCurrentTagIds, initiallyEditing = false }: ProductTagsProps) {
  const { t } = useTranslation();
  const titleId = useId();
  const pickId = useId();
  const [editing, setEditing] = useState(initiallyEditing);
  const [draft, setDraft] = useState<string[]>(() => tags.map((tag) => tag.id));
  const [newName, setNewName] = useState("");
  const [saving, setSaving] = useState(false);
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // A tag created a moment ago may not be in `allTags` until the list refetches.
  const [created, setCreated] = useState<ProductTag[]>([]);

  const choices = [
    ...allTags,
    ...created.filter((tag) => !allTags.some((entry) => entry.id === tag.id))
  ];
  const full = draft.length >= maximumTagsPerProduct;
  const currentIds = tags.map((tag) => tag.id);
  // Suggestions only make sense when some household tag is not on the product yet.
  const canSuggest = onSuggest !== undefined
    && tags.length < maximumTagsPerProduct
    && allTags.some((tag) => !currentIds.includes(tag.id));

  // Accepting the last suggestion that could still apply removes the whole
  // suggestions block together with the focused button; land on the section
  // heading instead of the page body. Focus that is elsewhere is left alone.
  const headingRef = useRef<HTMLHeadingElement>(null);
  const couldSuggest = useRef(canSuggest);
  useEffect(() => {
    const lostFocus = document.activeElement === null || document.activeElement === document.body;
    if (couldSuggest.current && !canSuggest && !editing && lostFocus) headingRef.current?.focus();
    couldSuggest.current = canSuggest;
  }, [canSuggest, editing]);

  function startEditing() {
    setDraft(tags.map((tag) => tag.id));
    setError(null);
    setEditing(true);
  }

  function toggle(tagId: string) {
    setDraft((current) => current.includes(tagId)
      ? current.filter((id) => id !== tagId)
      : current.length >= maximumTagsPerProduct ? current : [...current, tagId]);
  }

  async function save() {
    setSaving(true);
    setError(null);
    try {
      await onSave(draft);
      setEditing(false);
    } catch {
      setError(t("tags.saveError"));
    } finally {
      setSaving(false);
    }
  }

  async function create(event: FormEvent) {
    event.preventDefault();
    const name = newName.trim();
    if (name === "") return;
    const key = tagKey(name);
    const existing = choices.find((tag) => tagKey(tag.name) === key);
    if (existing) {
      // Typing a name that already exists just picks that tag.
      if (!draft.includes(existing.id)) toggle(existing.id);
      setNewName("");
      return;
    }

    setCreating(true);
    setError(null);
    try {
      const tag = await onCreate(name);
      setCreated((current) => [...current, tag]);
      setDraft((current) => current.length >= maximumTagsPerProduct ? current : [...current, tag.id]);
      setNewName("");
    } catch (failure) {
      setError(failure instanceof ApiError && failure.status === 409 ? t("tags.duplicate") : t("tags.createError"));
    } finally {
      setCreating(false);
    }
  }

  return (
    <Surface as="section" className={styles.section} aria-labelledby={titleId}>
      <div className={styles.heading}>
        <h2 id={titleId} ref={headingRef} tabIndex={-1}>{t("tags.title")}</h2>
        {!editing && <IconButton intent="secondary" iconOnly="mobile" icon={<Pencil />} label={t("tags.edit")} onClick={startEditing} />}
      </div>

      {!editing && (
        <>
          <TagChips tags={tags} showEmpty />
          {canSuggest && onSuggest && (
            <TagSuggestions
              onSuggest={onSuggest}
              onAccept={(tagIds) => {
                const latest = readCurrentTagIds?.() ?? currentIds;
                return onSave([...latest, ...tagIds.filter((id) => !latest.includes(id))]);
              }}
              currentTagIds={currentIds}
              remainingSlots={maximumTagsPerProduct - tags.length}
            />
          )}
          <HelpLink topic="tags" />
        </>
      )}

      {editing && (
        <div className={styles.editor}>
          <div role="group" aria-labelledby={pickId} className={styles.pickGroup}>
            <p id={pickId} className={styles.pickLabel}>
              {t("tags.pickLabel")}
              <span className={styles.counter}>{t("tags.selectedCount", { count: draft.length, max: maximumTagsPerProduct })}</span>
            </p>
            {choices.length === 0
              ? <p className={styles.hint}>{t("tags.noTagsYet")}</p>
              : (
                <ul className={styles.toggles}>
                  {choices.map((tag) => {
                    const selected = draft.includes(tag.id);
                    return (
                      <li key={tag.id}>
                        <button
                          type="button"
                          className={styles.toggle}
                          aria-pressed={selected}
                          disabled={saving || (!selected && full)}
                          onClick={() => toggle(tag.id)}
                        >
                          {selected && <Check aria-hidden="true" size={14} strokeWidth={2.5} />}
                          <span className={styles.chipLabel}>{tag.name}</span>
                        </button>
                      </li>
                    );
                  })}
                </ul>
              )}
            {full && <p className={styles.hint}>{t("tags.limitReached", { max: maximumTagsPerProduct })}</p>}
          </div>

          <form className={styles.create} onSubmit={create}>
            <Field
              label={t("tags.newLabel")}
              placeholder={t("tags.newPlaceholder")}
              value={newName}
              maxLength={maximumTagNameLength}
              disabled={saving || creating}
              onChange={(event) => setNewName(event.currentTarget.value)}
            />
            <Button
              type="submit"
              intent="secondary"
              loading={creating}
              disabled={saving || newName.trim() === "" || full}
              leftSection={<Plus aria-hidden="true" size={16} />}
            >
              {t("tags.create")}
            </Button>
          </form>

          <p className={styles.hint}>{t("tags.appliesToAll")}</p>
          <div className={styles.actions}>
            <Button loading={saving} disabled={creating} onClick={() => void save()}>{t("tags.save")}</Button>
            <Button intent="quiet" onClick={() => setEditing(false)} disabled={saving}>{t("tags.cancel")}</Button>
          </div>
        </div>
      )}

      {error && <p className={styles.error} role="alert">{error}</p>}
    </Surface>
  );
}
