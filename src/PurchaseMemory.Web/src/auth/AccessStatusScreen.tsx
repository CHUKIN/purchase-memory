import { Clock3, RefreshCw, ServerCrash, ShieldOff, WifiOff, Wrench } from "lucide-react";
import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, EmptyState } from "../design-system/components";
import { AuthLayout } from "./AuthLayout";
import styles from "./LoginScreen.module.css";

export type AccessScreenKind = "pending" | "blocked" | "unconfigured" | "configError" | "connectionError" | "checking";

/** After this long "checking" explains that a sleeping server is waking up. */
export const slowCheckDelayMs = 4_000;

export interface AccessStatusScreenProps {
  kind: AccessScreenKind;
  email?: string | null;
  busy?: boolean;
  /** Connection error: a stored session exists and is kept (changes the wording). */
  sessionKept?: boolean;
  /** Pending: refresh the token and ask again. Config error: reload settings. */
  onRetry?: () => void;
  onSignOut?: () => void;
}

export function AccessStatusScreen({ kind, email = null, busy = false, sessionKept = false, onRetry, onSignOut }: AccessStatusScreenProps) {
  const { t } = useTranslation();
  const address = email ?? t("auth.unknownEmail");

  if (kind === "checking") return <CheckingScreen />;

  const content = {
    pending: { icon: <Clock3 />, title: t("auth.pendingTitle"), description: t("auth.pendingDescription", { email: address }), retry: t("auth.checkAgain") },
    blocked: { icon: <ShieldOff />, title: t("auth.blockedTitle"), description: t("auth.blockedDescription", { email: address }), retry: null },
    unconfigured: { icon: <Wrench />, title: t("auth.unconfiguredTitle"), description: t("auth.unconfiguredDescription"), retry: null },
    configError: { icon: <ServerCrash />, title: t("auth.configFailed"), description: "", retry: t("auth.retry") },
    connectionError: { icon: <WifiOff />, title: t("auth.connectionTitle"), description: t(sessionKept ? "auth.connectionDescription" : "auth.connectionDescriptionNeutral"), retry: t("auth.retry") }
  }[kind];

  return (
    <AuthLayout>
      <div role={kind === "blocked" || kind === "configError" || kind === "connectionError" ? "alert" : "status"}>
        <EmptyState
          headingLevel={1}
          icon={content.icon}
          title={content.title}
          description={content.description}
          action={
            <div className={styles.actions}>
              {content.retry && onRetry && (
                <Button intent="primary" loading={busy} leftSection={<RefreshCw aria-hidden="true" size={18} />} onClick={onRetry}>
                  {content.retry}
                </Button>
              )}
              {onSignOut && <Button intent="quiet" onClick={onSignOut}>{t("auth.signOut")}</Button>}
            </div>
          }
        />
      </div>
    </AuthLayout>
  );
}

function CheckingScreen() {
  const { t } = useTranslation();
  const [slow, setSlow] = useState(false);
  useEffect(() => {
    const timer = setTimeout(() => setSlow(true), slowCheckDelayMs);
    return () => clearTimeout(timer);
  }, []);
  return (
    <AuthLayout>
      <div role="status" className={styles.checking}>
        <p className={styles.intro}>{t("auth.checking")}</p>
        {slow && <p className={styles.hint}>{t("auth.checkingSlow")}</p>}
      </div>
    </AuthLayout>
  );
}
