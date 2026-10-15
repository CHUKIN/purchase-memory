import { IonButton, IonItem, IonLabel, IonList } from "@ionic/react";
import { CircleHelp, X } from "lucide-react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { helpPath } from "./topics";

export const helpWelcomeStorageKey = "purchase-memory.help-welcome-dismissed";

// Storage can be missing or throw (private mode, blocked site data); the card
// then simply shows again next time, which is harmless.
function readDismissed(): boolean {
  try {
    return window.localStorage.getItem(helpWelcomeStorageKey) === "1";
  } catch {
    return false;
  }
}

function writeDismissed() {
  try {
    window.localStorage.setItem(helpWelcomeStorageKey, "1");
  } catch {
    // Nothing to do: dismissal just lasts for this visit.
  }
}

/** A light first-run pointer to the help section, hidden for good once dismissed. */
export function HelpWelcomeCard() {
  const { t } = useTranslation();
  const [dismissed, setDismissed] = useState(readDismissed);
  if (dismissed) return null;

  return (
    <aside aria-label={t("help.ui.welcomeTitle")}>
      <IonList inset>
        <IonItem lines="none">
          <CircleHelp slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
          <IonLabel>
            <Link className="pm-text-action" to={helpPath()}>{t("help.ui.welcomeOpen")}</Link>
          </IonLabel>
          <IonButton
            slot="end"
            fill="clear"
            color="medium"
            className="pm-icon-button"
            aria-label={t("help.ui.welcomeDismiss")}
            onClick={() => { writeDismissed(); setDismissed(true); }}
          >
            <X aria-hidden="true" size={20} />
          </IonButton>
        </IonItem>
      </IonList>
    </aside>
  );
}
