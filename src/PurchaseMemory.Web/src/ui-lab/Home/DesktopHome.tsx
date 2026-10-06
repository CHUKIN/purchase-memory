import { AlertTriangle, ChevronRight, ShoppingBasket, Sparkles, ReceiptText } from "lucide-react";
import { useTranslation } from "react-i18next";
import { MemoryRouter } from "react-router";
import { AppShell } from "../../app/AppShell/AppShell";
import { Button, StatusBadge, Surface } from "../../design-system/components";
import { recentPurchases } from "../mockData";
import { PurchaseCard } from "../PurchaseCard/PurchaseCard";
import styles from "./Home.module.css";

export function DesktopHome() {
  const { t } = useTranslation();
  const queue = [
    { purchase: recentPurchases[1], reason: t("home.reasonPrice") },
    { purchase: { ...recentPurchases[0], merchant: "Auchan", total: 73.81 }, reason: t("home.reasonMissing") },
    { purchase: recentPurchases[2], reason: t("home.reasonDuplicate") }
  ];

  return (
    <MemoryRouter initialEntries={["/"]}>
      <AppShell recognition={{ available: true, mode: "UI Lab", models: ["mock"], message: null }}>
      <div className={styles.desktopMain}>
        <section className={styles.desktopHero}>
          <div>
            <span>{t("home.desktopEyebrow")}</span>
            <h1>{t("home.desktopTitle")}</h1>
            <p>{t("home.desktopIntro")}</p>
          </div>
          <div className={styles.heroIllustration} aria-hidden="true"><Sparkles size={26} /><ReceiptText size={42} /></div>
        </section>

        <section className={styles.metrics} aria-label={t("home.desktopEyebrow")}>
          <Surface density="compact"><span>{t("home.monthSpent")}</span><strong>1 284,70 zł</strong><small>{t("home.monthTrend")}</small></Surface>
          <Surface density="compact"><span>{t("home.monthPurchases")}</span><strong>18</strong><small>{t("home.verifiedCount")}</small></Surface>
          <Surface density="compact"><span>{t("home.avgReceipt")}</span><strong>71,37 zł</strong><small>{t("home.storeCount")}</small></Surface>
        </section>

        <div className={styles.desktopColumns}>
          <Surface as="section" className={styles.reviewPanel} aria-labelledby="review-queue-heading">
            <div className={styles.panelHeading}>
              <div><span>{t("common.needsAttention")}</span><h2 id="review-queue-heading">{t("home.reviewQueue")}</h2></div>
              <StatusBadge status="needsAttention" />
            </div>
            <p>{t("home.reviewIntro")}</p>
            <div className={styles.queueList}>
              {queue.map(({ purchase, reason }, index) => (
                <article key={`${purchase.id}-${index}`} className={styles.queueRow}>
                  <div className={styles.queueIcon}><AlertTriangle aria-hidden="true" size={18} /></div>
                  <div><strong>{purchase.merchant}</strong><span>{reason}</span></div>
                  <b>{new Intl.NumberFormat("pl-PL", { style: "currency", currency: "PLN" }).format(purchase.total)}</b>
                  <button type="button" aria-label={`${t("common.open")} ${purchase.merchant}`}><ChevronRight aria-hidden="true" size={19} /></button>
                </article>
              ))}
            </div>
            <Button intent="secondary" fullWidth>{t("common.viewAll")}</Button>
          </Surface>

          <section className={styles.desktopRecent} aria-labelledby="desktop-recent-heading">
            <div className={styles.sectionTitle}>
              <div><ShoppingBasket aria-hidden="true" size={18} /><h2 id="desktop-recent-heading">{t("common.recentPurchases")}</h2></div>
              <Button intent="quiet">{t("common.viewAll")}</Button>
            </div>
            <PurchaseCard purchase={recentPurchases[0]} density="compact" />
            <PurchaseCard purchase={recentPurchases[1]} density="compact" />
          </section>
        </div>
      </div>
      </AppShell>
    </MemoryRouter>
  );
}
