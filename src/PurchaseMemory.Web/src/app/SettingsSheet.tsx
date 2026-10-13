import { IonActionSheet, IonItem, IonLabel, IonList, IonNote, IonSegment, IonSegmentButton } from "@ionic/react";
import { CircleHelp, LogOut } from "lucide-react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import type { RecognitionStatus } from "../api/types";
import { useAuth } from "../auth/AuthContext";
import { ItemLink, SectionHeader, Sheet } from "../design-system/components";
import { helpPath } from "../features/help/topics";
import { isAppLocale } from "../i18n/i18n";
import { LANGUAGE_SWITCH_ENABLED, useAppLocale } from "./LocaleContext";

export interface SettingsSheetProps {
  isOpen: boolean;
  onClose: () => void;
  recognition: RecognitionStatus | null;
  recognitionFailed?: boolean;
}

/**
 * «Ещё» on Home: the rarely used things that used to crowd every screen's
 * header — help, recognition status and sign-out.
 */
export function SettingsSheet({ isOpen, onClose, recognition, recognitionFailed = false }: SettingsSheetProps) {
  const { t } = useTranslation();
  const { locale, setLocale } = useAppLocale();
  const auth = useAuth();
  const [confirmSignOut, setConfirmSignOut] = useState(false);
  const recognitionText = recognition === null && !recognitionFailed
    ? t("shell.recognitionChecking")
    : recognition?.available && !recognitionFailed
      ? t("shell.recognitionAvailable")
      : t("shell.recognitionUnavailable");

  return (
    <Sheet isOpen={isOpen} onClose={onClose} title={t("settings.title")}>
      <IonList inset>
        <ItemLink to={helpPath()} onClick={onClose}>
          <CircleHelp slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
          <IonLabel>{t("settings.help")}</IonLabel>
        </ItemLink>
      </IonList>

      {LANGUAGE_SWITCH_ENABLED && (
        <>
      <SectionHeader level={3}>{t("settings.language")}</SectionHeader>
      <IonList inset className="pm-after-header">
        <div className="ion-padding">
          <IonSegment
            value={locale}
            aria-label={t("shell.interfaceLanguage")}
            onIonChange={(event) => { if (isAppLocale(event.detail.value)) setLocale(event.detail.value); }}
          >
            <IonSegmentButton value="ru"><IonLabel>Русский</IonLabel></IonSegmentButton>
            <IonSegmentButton value="en"><IonLabel>English</IonLabel></IonSegmentButton>
          </IonSegment>
        </div>
      </IonList>
        </>
      )}

      <IonList inset>
        <IonItem>
          <IonLabel className="pm-wrap">{t("settings.recognition")}</IonLabel>
          <IonNote slot="end" className="pm-pick-value pm-wrap">{recognitionText}</IonNote>
        </IonItem>
      </IonList>

      {auth && (
        <>
          <SectionHeader level={3}>{t("settings.account")}</SectionHeader>
          <IonList inset className="pm-after-header">
            <IonItem lines={auth.signOut ? undefined : "none"}>
              <IonLabel className="pm-wrap">{t("auth.signedInAs", { email: auth.user.email ?? t("auth.unknownEmail") })}</IonLabel>
            </IonItem>
            {/* The local development bypass has no session to end. */}
            {auth.signOut && (
              <IonItem button detail={false} onClick={() => setConfirmSignOut(true)}>
                <LogOut slot="start" size={22} className="pm-row-icon pm-danger-icon" aria-hidden="true" />
                <IonLabel color="danger">{t("settings.signOut")}</IonLabel>
              </IonItem>
            )}
          </IonList>
          <IonActionSheet
            isOpen={confirmSignOut}
            header={t("settings.signOutConfirm")}
            onDidDismiss={() => setConfirmSignOut(false)}
            buttons={[
              { text: t("settings.signOut"), role: "destructive", handler: () => { void auth.signOut?.(); } },
              { text: t("ui.cancel"), role: "cancel" }
            ]}
          />
        </>
      )}
    </Sheet>
  );
}
