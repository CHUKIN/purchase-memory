import { IonActionSheet, IonAlert, IonItem, IonLabel, IonNote } from "@ionic/react";
import { useState } from "react";

export interface ManagedRowText {
  rename: string;
  delete: string;
  cancel: string;
  save: string;
  deleteConfirm: string;
  deleteYes: string;
  /** Why deleting is not possible right now, e.g. «Сначала удалите подкатегории». */
  deleteBlocked?: string;
}

export interface ManagedRowProps {
  label: string;
  detail: string;
  /** Indents a subcategory under its group. */
  nested?: boolean;
  /** Draws a group in bold. */
  strong?: boolean;
  disabled?: boolean;
  maxLength: number;
  text: ManagedRowText;
  onRename: (name: string) => void;
  onDelete: () => void;
  last?: boolean;
}

/**
 * One editable entry of a household list (a category or a tag). A tap opens
 * an action sheet with «Переименовать» and «Удалить»; renaming asks for the
 * new name in an alert, deleting asks to confirm, the iOS way.
 */
export function ManagedRow({ label, detail, nested = false, strong = false, disabled = false, maxLength, text, onRename, onDelete, last = false }: ManagedRowProps) {
  const [step, setStep] = useState<"actions" | "rename" | "confirm" | null>(null);
  const blocked = text.deleteBlocked !== undefined;

  return (
    <>
      <IonItem button detail={false} disabled={disabled} lines={last ? "none" : undefined} className={nested ? "pm-nested-row" : undefined} onClick={() => setStep("actions")}>
        <IonLabel className="pm-wrap">{strong ? <strong>{label}</strong> : label}</IonLabel>
        <IonNote slot="end">{detail}</IonNote>
      </IonItem>
      <IonActionSheet
        isOpen={step === "actions"}
        header={label}
        subHeader={text.deleteBlocked}
        onDidDismiss={() => setStep((current) => current === "actions" ? null : current)}
        buttons={[
          { text: text.rename, handler: () => { setStep("rename"); } },
          ...(blocked ? [] : [{ text: text.delete, role: "destructive", handler: () => { setStep("confirm"); } }]),
          { text: text.cancel, role: "cancel" }
        ]}
      />
      <IonAlert
        isOpen={step === "rename"}
        header={text.rename}
        subHeader={label}
        inputs={[{ name: "name", type: "text", value: label, attributes: { maxlength: maxLength }, label: text.rename }]}
        buttons={[
          { text: text.cancel, role: "cancel" },
          {
            text: text.save,
            handler: (values: { name?: string }) => {
              const name = (values.name ?? "").trim();
              if (name !== "" && name !== label) onRename(name);
            }
          }
        ]}
        onDidDismiss={() => setStep(null)}
      />
      <IonActionSheet
        isOpen={step === "confirm"}
        header={text.deleteConfirm}
        onDidDismiss={() => setStep(null)}
        buttons={[
          { text: text.deleteYes, role: "destructive", handler: onDelete },
          { text: text.cancel, role: "cancel" }
        ]}
      />
    </>
  );
}
