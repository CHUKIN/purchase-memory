import { IonButton, IonCheckbox, IonItem, IonList } from "@ionic/react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Footnote, Page, SectionHeader } from "../../design-system/components";
import { checklistGroups, checklistItemIds } from "./checklistData";
import { readChecked, writeChecked } from "./checklistStorage";

/**
 * «Что протестировать»: a plain checklist of what to try on the phone.
 * Ticks live in this device's localStorage only; there are no server calls.
 */
export function TestChecklistPage() {
  const { t } = useTranslation();
  const [checked, setChecked] = useState<string[]>(readChecked);
  // Ids that were removed from the list later do not count.
  const done = checklistItemIds.filter((id) => checked.includes(id)).length;

  function update(next: string[]) {
    setChecked(next);
    writeChecked(next);
  }

  function toggle(id: string, on: boolean) {
    update(on ? [...checked.filter((other) => other !== id), id] : checked.filter((other) => other !== id));
  }

  return (
    <Page title={t("testChecklist.title")} back={{ to: "/", label: t("ui.back"), preferHistory: true }}>
      <p className="pm-home-intro">{t("testChecklist.intro")}</p>
      <p className="pm-home-intro" role="status">{t("testChecklist.progress", { done, total: checklistItemIds.length })}</p>

      {checklistGroups.map((group) => (
        <section key={group.id} aria-labelledby={`test-checklist-${group.id}`}>
          <SectionHeader id={`test-checklist-${group.id}`}>{t(`testChecklist.groups.${group.id}`)}</SectionHeader>
          <IonList inset className="pm-after-header">
            {group.items.map((id) => (
              <IonItem key={id} lines="full">
                <IonCheckbox
                  className="pm-check-row"
                  checked={checked.includes(id)}
                  justify="start"
                  labelPlacement="end"
                  onIonChange={(event) => toggle(id, event.detail.checked)}
                >
                  <span className="pm-wrap pm-check-text">
                    <span className="pm-check-title">{t(`testChecklist.items.${id}.title`)}</span>
                    <span className="pm-check-description">{t(`testChecklist.items.${id}.description`)}</span>
                  </span>
                </IonCheckbox>
              </IonItem>
            ))}
          </IonList>
        </section>
      ))}

      <Footnote>{t("testChecklist.outro")}</Footnote>

      <div className="pm-check-reset">
        <IonButton fill="clear" disabled={done === 0 && checked.length === 0} onClick={() => update([])}>
          {t("testChecklist.reset")}
        </IonButton>
      </div>
    </Page>
  );
}
