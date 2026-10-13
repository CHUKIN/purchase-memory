import { IonButton, IonSpinner } from "@ionic/react";
import { Clock3, RefreshCw, ServerCrash, ShieldOff, WifiOff, Wrench } from "lucide-react";
import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { ActionButton, EmptyState, Footnote } from "../design-system/components";
import { AuthLayout } from "./AuthLayout";

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
          description={content.description || undefined}
          action={
            <div className="pm-auth-actions">
              {content.retry && onRetry && (
                <ActionButton expand="block" shape="round" loading={busy} onClick={onRetry}>
                  <RefreshCw aria-hidden="true" size={18} />&nbsp;{content.retry}
                </ActionButton>
              )}
              {onSignOut && <IonButton expand="block" fill="clear" onClick={onSignOut}>{t("auth.signOut")}</IonButton>}
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
      <div role="status" className="pm-auth-checking">
        <IonSpinner name="crescent" aria-hidden="true" />
        <p>{t("auth.checking")}</p>
        {slow && <Footnote>{t("auth.checkingSlow")}</Footnote>}
      </div>
    </AuthLayout>
  );
}
