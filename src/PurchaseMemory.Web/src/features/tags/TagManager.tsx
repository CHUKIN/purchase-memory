import { IonInput, IonItem, IonLabel, IonList } from "@ionic/react";
import { Plus } from "lucide-react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { ApiError } from "../../api/client";
import type { TagSummary } from "../../api/types";
import { Footnote, SectionHeader } from "../../design-system/components";
import { ManagedRow } from "../categories/ManagedRow";
import { maximumTagNameLength } from "./ProductTags";

export interface TagManagerProps {
  tags: TagSummary[];
  onCreate: (name: string) => Promise<unknown>;
  onRename: (tagId: string, name: string) => Promise<unknown>;
  onDelete: (tagId: string) => Promise<unknown>;
}

/**
 * Where the household creates, renames and deletes its tags. A deleted tag
 * simply disappears from its products; the products themselves stay.
 */
export function TagManager({ tags, onCreate, onRename, onDelete }: TagManagerProps) {
  const { t } = useTranslation();
  const [name, setName] = useState("");
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

  async function create() {
    if (name.trim() === "") return;
    if (await run(() => onCreate(name.trim()))) setName("");
  }

  return (
    <>
      <Footnote>{t("tags.manageIntro")}</Footnote>

      <SectionHeader level={3}>{t("tags.add")}</SectionHeader>
      <IonList inset className="pm-after-header">
        <IonItem>
          <IonInput
            label={t("tags.newLabel")}
            labelPlacement="stacked"
            placeholder={t("tags.newPlaceholder")}
            value={name}
            maxlength={maximumTagNameLength}
            disabled={busy}
            onIonInput={(event) => setName(String(event.detail.value ?? ""))}
            onKeyDown={(event) => { if (event.key === "Enter") void create(); }}
          />
        </IonItem>
        <IonItem button detail={false} lines="none" disabled={busy || name.trim() === ""} onClick={() => void create()}>
          <Plus slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
          <IonLabel color="primary">{t("tags.add")}</IonLabel>
        </IonItem>
      </IonList>

      {error && <Footnote tone="danger" role="alert">{error}</Footnote>}

      {tags.length === 0
        ? <Footnote>{t("tags.noTagsYet")}</Footnote>
        : (
          <IonList inset aria-label={t("tags.listLabel")}>
            {tags.map((tag, index) => (
              <ManagedRow
                key={tag.id}
                label={tag.name}
                detail={t("tags.productCount", { count: tag.productCount })}
                disabled={busy}
                maxLength={maximumTagNameLength}
                last={index === tags.length - 1}
                text={{
                  rename: t("tags.rename"),
                  delete: t("tags.delete"),
                  cancel: t("tags.cancel"),
                  save: t("tags.save"),
                  deleteConfirm: t("tags.deleteConfirm", { name: tag.name }),
                  deleteYes: t("tags.deleteYes")
                }}
                onRename={(next) => void run(() => onRename(tag.id, next))}
                onDelete={() => void run(() => onDelete(tag.id))}
              />
            ))}
          </IonList>
        )}
    </>
  );
}
