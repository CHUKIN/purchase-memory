import { IonAlert, IonButton, IonItem, IonLabel, IonList } from "@ionic/react";
import { Pencil } from "lucide-react";
import { useState, type ReactNode } from "react";
import { ApiError } from "../../api/client";
import { useTranslation } from "react-i18next";
import type { AttributeSource, ProductNameLanguage } from "../../api/types";
import { ActionButton, Footnote, SectionHeader } from "../../design-system/components";

/** Longest name the server takes; longer is refused, not cut. */
export const maximumProductNameLength = 200;

export interface ProductNamesProps {
  namePl: string | null;
  namePlSource: AttributeSource | null;
  nameRu: string | null;
  nameRuSource: AttributeSource | null;
  /** Saves one name as the person's (empty clears it: the receipt text shows). */
  onSave: (language: ProductNameLanguage, name: string) => Promise<unknown>;
  /** «Верно»: confirms every suggested name as it is. */
  onConfirm: () => Promise<unknown>;
  /** «Предложить заново»: asks the AI again; confirmed names stay. Hidden without it. */
  onSuggest?: () => Promise<unknown>;
  /** A «?» (HelpLink) at the end of the section header. */
  help?: ReactNode;
}

type Busy = "save" | "confirm" | "suggest" | null;

/**
 * «Названия»: the Polish (shelf) and Russian name of one product. Each can be
 * edited; an AI suggestion carries «предложено» until «Верно». A name typed or
 * confirmed by a person is never replaced by the AI, so «Предложить заново»
 * only shows while some name is still the AI's (or missing).
 */
export function ProductNames({ namePl, namePlSource, nameRu, nameRuSource, onSave, onConfirm, onSuggest, help }: ProductNamesProps) {
  const { t } = useTranslation();
  const [busy, setBusy] = useState<Busy>(null);
  const [error, setError] = useState<"save" | "suggest" | "changed" | null>(null);
  const [editing, setEditing] = useState<ProductNameLanguage | null>(null);
  const suggested = (namePl !== null && namePlSource === "suggested") || (nameRu !== null && nameRuSource === "suggested");
  const canSuggest = onSuggest !== undefined && (namePlSource !== "confirmed" || nameRuSource !== "confirmed");

  async function run(kind: Exclude<Busy, null>, action: () => Promise<unknown>) {
    setBusy(kind);
    setError(null);
    try {
      await action();
    } catch (failure) {
      setError(kind === "suggest" ? "suggest" : kind === "confirm" && failure instanceof ApiError && failure.status === 409 ? "changed" : "save");
    } finally {
      setBusy(null);
    }
  }

  const row = (language: ProductNameLanguage, value: string | null, source: AttributeSource | null, last: boolean) => (
    <IonItem lines={last ? "none" : undefined}>
      <IonLabel className="pm-wrap">
        <p className="pm-name-label">
          {language === "pl" ? t("products.names.polish") : t("products.names.russian")}
          {value !== null && source === "suggested" && <>{" "}<span className="pm-suggested-chip">{t("products.names.suggested")}</span></>}
        </p>
        <h3 className={value === null ? "pm-name-empty" : undefined}>{value ?? t("products.names.none")}</h3>
      </IonLabel>
      <IonButton
        slot="end"
        fill="clear"
        className="pm-icon-button"
        disabled={busy !== null}
        onClick={() => setEditing(language)}
        aria-label={language === "pl" ? t("products.names.editPolish") : t("products.names.editRussian")}
      >
        <Pencil size={20} aria-hidden="true" />
      </IonButton>
    </IonItem>
  );

  const showActions = suggested || canSuggest;
  return (
    <section aria-labelledby="product-names-title">
      <SectionHeader id="product-names-title" action={help}>{t("products.names.title")}</SectionHeader>
      <IonList inset className="pm-after-header">
        {row("pl", namePl, namePlSource, false)}
        {row("ru", nameRu, nameRuSource, !showActions)}
        {showActions && (
          <div className="pm-names-actions">
            {suggested && (
              <ActionButton className="pm-names-action" loading={busy === "confirm"} disabled={busy !== null} onClick={() => void run("confirm", onConfirm)}>
                {t("products.names.confirm")}
              </ActionButton>
            )}
            {canSuggest && (
              <ActionButton
                className="pm-names-action"
                fill="outline"
                loading={busy === "suggest"}
                disabled={busy !== null}
                onClick={() => void run("suggest", onSuggest!)}
              >
                {t("products.names.suggestAgain")}
              </ActionButton>
            )}
          </div>
        )}
      </IonList>
      {error === "save" && <Footnote tone="danger" role="alert">{t("products.names.saveError")}</Footnote>}
      {error === "suggest" && <Footnote tone="danger" role="alert">{t("products.names.suggestError")}</Footnote>}
      {error === "changed" && <Footnote tone="danger" role="alert">{t("products.names.changedError")}</Footnote>}

      <IonAlert
        isOpen={editing !== null}
        header={editing === "ru" ? t("products.names.russian") : t("products.names.polish")}
        message={t("products.names.editHint")}
        inputs={[{
          name: "name",
          type: "text",
          value: (editing === "ru" ? nameRu : namePl) ?? "",
          placeholder: editing === "ru" ? t("products.names.placeholderRu") : t("products.names.placeholderPl"),
          attributes: { maxlength: maximumProductNameLength, "aria-label": editing === "ru" ? t("products.names.russian") : t("products.names.polish") }
        }]}
        buttons={[
          { text: t("products.names.cancel"), role: "cancel" },
          {
            text: t("products.names.save"),
            handler: (values: { name?: string }) => {
              const language = editing ?? "pl";
              void run("save", () => onSave(language, (values.name ?? "").trim()));
            }
          }
        ]}
        onDidDismiss={() => setEditing(null)}
      />
    </section>
  );
}
