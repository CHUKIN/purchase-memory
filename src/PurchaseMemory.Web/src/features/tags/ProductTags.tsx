import { IonButton, IonCheckbox, IonInput, IonItem, IonLabel, IonList } from "@ionic/react";
import { Pencil, Plus } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { ApiError } from "../../api/client";
import type { ProductTag, TagSuggestionsResponse, TagSummary } from "../../api/types";
import { ActionButton, Footnote, Sheet } from "../../design-system/components";
import { HelpLink } from "../help/HelpLink";
import { TagChips } from "./TagChips";
import { TagSuggestions } from "./TagSuggestions";
import { tagKey } from "./tagKey";

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
  /** Starts with the editor open; for stories and tests. */
  initiallyEditing?: boolean;
}

/**
 * The product's tags: extra, overlapping slices next to the one category.
 * «Изменить теги» opens a sheet with every household tag as a checkbox row
 * and a field to create a new tag, which is picked straight away.
 */
export function ProductTags({ tags, allTags, onSave, onCreate, onSuggest, readCurrentTagIds, initiallyEditing = false }: ProductTagsProps) {
  const { t } = useTranslation();
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

  async function create() {
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
    <section aria-labelledby="product-tags-title">
      <div className="pm-section-header">
        <h2 id="product-tags-title" ref={headingRef} tabIndex={-1}>{t("tags.title")}</h2>
        <HelpLink topic="tags" />
      </div>
      <IonList inset className="pm-after-header">
        <IonItem>
          <IonLabel className="pm-wrap"><TagChips tags={tags} showEmpty /></IonLabel>
        </IonItem>
        <IonItem button detail lines={canSuggest ? undefined : "none"} onClick={startEditing}>
          <Pencil slot="start" size={20} className="pm-row-icon" aria-hidden="true" />
          <IonLabel color="primary">{t("tags.edit")}</IonLabel>
        </IonItem>
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
      </IonList>
      {!editing && error && <Footnote tone="danger" role="alert">{error}</Footnote>}

      <Sheet
        isOpen={editing}
        onClose={() => setEditing(false)}
        title={t("tags.edit")}
        variant="full"
        start={<IonButton disabled={saving} onClick={() => setEditing(false)}>{t("tags.cancel")}</IonButton>}
        end={<ActionButton strong loading={saving} disabled={creating} onClick={() => void save()}>{t("tags.save")}</ActionButton>}
      >
        <Footnote id="product-tags-pick"><span className="visually-hidden">{t("tags.pickLabel")} · </span>{t("tags.selectedCount", { count: draft.length, max: maximumTagsPerProduct })}</Footnote>
        {choices.length === 0
          ? <Footnote>{t("tags.noTagsYet")}</Footnote>
          : (
            <IonList inset role="group" aria-labelledby="product-tags-pick">
              {choices.map((tag, index) => {
                const selected = draft.includes(tag.id);
                return (
                  <IonItem key={tag.id} lines={index === choices.length - 1 ? "none" : undefined}>
                    <IonCheckbox
                      checked={selected}
                      disabled={saving || (!selected && full)}
                      justify="space-between"
                      onIonChange={() => toggle(tag.id)}
                    >
                      <span className="pm-wrap">{tag.name}</span>
                    </IonCheckbox>
                  </IonItem>
                );
              })}
            </IonList>
          )}
        {full && <Footnote>{t("tags.limitReached", { max: maximumTagsPerProduct })}</Footnote>}

        <IonList inset>
          <IonItem>
            <IonInput
              label={t("tags.newLabel")}
              labelPlacement="stacked"
              placeholder={t("tags.newPlaceholder")}
              value={newName}
              maxlength={maximumTagNameLength}
              disabled={saving || creating}
              onIonInput={(event) => setNewName(String(event.detail.value ?? ""))}
              onKeyDown={(event) => { if (event.key === "Enter") void create(); }}
            />
          </IonItem>
          <IonItem button detail={false} lines="none" disabled={saving || creating || newName.trim() === "" || full} onClick={() => void create()}>
            <Plus slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
            <IonLabel color="primary">{t("tags.create")}</IonLabel>
          </IonItem>
        </IonList>
        {error && <Footnote tone="danger" role="alert">{error}</Footnote>}
      </Sheet>
    </section>
  );
}
