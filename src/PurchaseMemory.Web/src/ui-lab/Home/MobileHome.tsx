import { Camera, Clock3, ReceiptText } from "lucide-react";
import { useTranslation } from "react-i18next";
import { MemoryRouter } from "react-router";
import { AppShell } from "../../app/AppShell/AppShell";
import { Button, StatusBadge, Surface } from "../../design-system/components";
import { recentPurchases } from "../mockData";
import { PurchaseCard } from "../PurchaseCard/PurchaseCard";
import styles from "./Home.module.css";

export function MobileHome() {
  const { t } = useTranslation();

  return (
    <MemoryRouter initialEntries={["/"]}>
      <AppShell recognition={{ available: true, mode: "UI Lab", models: ["mock"], message: null }}>
      <div className={styles.mobileMain}>
        <section className={styles.journalHero}>
          <span>{t("home.greeting")}</span>
          <h1>{t("home.mobileTitle")}</h1>
          <p>{t("home.mobileIntro")}</p>
          <Button fullWidth className={styles.cameraCta} leftSection={<Camera aria-hidden="true" size={22} />}>
            {t("common.captureReceipt")}
          </Button>
        </section>

        <Surface as="section" className={styles.processingStrip} aria-label={t("status.processing")}>
          <span className={styles.processingGlyph}><ReceiptText aria-hidden="true" size={20} /></span>
          <div>
            <strong>{t("home.processingTitle")}</strong>
            <small>{t("home.processingMeta")}</small>
          </div>
          <StatusBadge status="processing" />
        </Surface>

        <section className={styles.mobileRecent} aria-labelledby="mobile-recent-heading">
          <div className={styles.sectionTitle}>
            <div>
              <Clock3 aria-hidden="true" size={18} />
              <h2 id="mobile-recent-heading">{t("common.recentPurchases")}</h2>
            </div>
            <Button intent="quiet">{t("common.viewAll")}</Button>
          </div>
          <PurchaseCard purchase={recentPurchases[0]} />
          <PurchaseCard purchase={recentPurchases[1]} />
        </section>
      </div>
      </AppShell>
    </MemoryRouter>
  );
}
