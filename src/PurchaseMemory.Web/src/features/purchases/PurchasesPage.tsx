import { useTranslation } from "react-i18next";
import { HelpLink } from "../help/HelpLink";
import styles from "./PurchasesPage.module.css";
import { PurchaseFeedSection } from "./PurchaseFeedSection";

export function PurchasesPage() {
  const { t } = useTranslation();

  return (
    <div className={styles.page}>
      <header className={styles.hero}>
        <p className={styles.eyebrow}>{t("feed.eyebrow")}</p>
        <h1>{t("feed.pageTitle")}</h1>
        <p className={styles.intro}>{t("feed.pageIntro")}</p>
        <HelpLink topic="feed" />
      </header>
      <PurchaseFeedSection />
    </div>
  );
}
