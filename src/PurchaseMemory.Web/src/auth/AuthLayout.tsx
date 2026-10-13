import { IonButtons, IonContent, IonHeader, IonLabel, IonSegment, IonSegmentButton, IonTitle, IonToolbar } from "@ionic/react";
import type { PropsWithChildren } from "react";
import { useTranslation } from "react-i18next";
import { useAppLocale } from "../app/LocaleContext";
import { isAppLocale } from "../i18n/i18n";

/**
 * The sign-in and access-state screens: the app name in the bar, the
 * language switch next to it (nobody is signed in yet to reach settings), and
 * a narrow column of grouped content.
 */
export function AuthLayout({ children }: PropsWithChildren) {
  const { t } = useTranslation();
  const { locale, setLocale } = useAppLocale();
  return (
    <div className="ion-page">
      <IonHeader>
        <IonToolbar>
          <IonTitle>
            <span className="pm-brand"><span className="pm-brand-mark" aria-hidden="true">PM</span>{t("common.appNameEnglish")}</span>
          </IonTitle>
          <IonButtons slot="end">
            <IonSegment
              className="pm-locale-switch"
              value={locale}
              aria-label={t("shell.interfaceLanguage")}
              onIonChange={(event) => { if (isAppLocale(event.detail.value)) setLocale(event.detail.value); }}
            >
              <IonSegmentButton value="ru"><IonLabel>RU</IonLabel></IonSegmentButton>
              <IonSegmentButton value="en"><IonLabel>EN</IonLabel></IonSegmentButton>
            </IonSegment>
          </IonButtons>
        </IonToolbar>
      </IonHeader>
      <IonContent color="light">
        <main className="pm-auth">{children}</main>
      </IonContent>
    </div>
  );
}
