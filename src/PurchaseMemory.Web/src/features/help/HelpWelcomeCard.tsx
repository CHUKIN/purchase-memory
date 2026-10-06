import { CircleHelp, X } from "lucide-react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { Surface } from "../../design-system/components";
import { helpPath } from "./topics";
import styles from "./Help.module.css";

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
    <Surface as="aside" tone="warm" className={styles.welcome} aria-labelledby="help-welcome-heading">
      <span className={styles.welcomeIcon} aria-hidden="true"><CircleHelp /></span>
      <div className={styles.welcomeCopy}>
        <h2 id="help-welcome-heading">{t("help.ui.welcomeTitle")}</h2>
        <p>{t("help.ui.welcomeBody")}</p>
        <Link className={styles.welcomeAction} to={helpPath()}>{t("help.ui.welcomeOpen")}</Link>
      </div>
      <button
        type="button"
        className={styles.welcomeDismiss}
        aria-label={t("help.ui.welcomeDismiss")}
        onClick={() => { writeDismissed(); setDismissed(true); }}
      >
        <X aria-hidden="true" size={18} />
      </button>
    </Surface>
  );
}
