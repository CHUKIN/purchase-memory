import type { PropsWithChildren } from "react";
import { useTranslation } from "react-i18next";
import { Surface } from "../design-system/components";
import { useAppLocale } from "../app/LocaleContext";
import styles from "./LoginScreen.module.css";

/** Centered card shared by the login and access-state screens. */
export function AuthLayout({ children }: PropsWithChildren) {
  const { t } = useTranslation();
  const { locale, setLocale } = useAppLocale();
  return (
    <main className={styles.page}>
      <Surface as="section" elevated className={styles.card}>
        <div className={styles.header}>
          <span className={styles.brand}>
            <span className={styles.brandMark} aria-hidden="true">PM</span>
            {t("common.appNameEnglish")}
          </span>
          <div className={styles.locale} role="group" aria-label={t("shell.interfaceLanguage")}>
            <button type="button" aria-pressed={locale === "ru"} onClick={() => setLocale("ru")}>RU</button>
            <button type="button" aria-pressed={locale === "en"} onClick={() => setLocale("en")}>EN</button>
          </div>
        </div>
        {children}
      </Surface>
    </main>
  );
}
