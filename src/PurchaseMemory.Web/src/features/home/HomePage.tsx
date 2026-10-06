import { AlertTriangle, Camera, History, ReceiptText } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { ReceiptStatus, ReceiptView, RecognitionStatus } from "../../api/types";
import { Surface } from "../../design-system/components";
import { HelpWelcomeCard } from "../help/HelpWelcomeCard";
import { UnratedPurchasesBlock } from "../impressions/ImpressionSections";
import { PurchaseFeedSection } from "../purchases/PurchaseFeedSection";
import styles from "./HomePage.module.css";

// uploaded -> queued -> processing -> ready/failed, used to drive the
// progress indicator on a receipt still in flight.
const progressSteps: ReceiptStatus[] = ["uploaded", "queued", "processing", "ready"];

function progressStep(status: ReceiptStatus): number {
  const index = progressSteps.indexOf(status);
  return index === -1 ? progressSteps.length - 1 : index; // "failed" also reads as the final step
}

export function HomePage({ recognition, recognitionFailed = false, receipts, receiptsFailed = false }: { recognition: RecognitionStatus | null; recognitionFailed?: boolean; receipts: ReceiptView[]; receiptsFailed?: boolean }) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage === "en" ? "en-GB" : "ru-RU";
  const available = recognition?.available === true && !recognitionFailed;
  const openReceipts = receipts.filter((receipt) => receipt.status !== "saved");
  const recognitionCopy = recognition === null && !recognitionFailed
    ? t("productionHome.recognitionLoading")
    : available
      ? t("productionHome.recognitionReady")
      : t("productionHome.recognitionOffline");

  return (
    <div className={styles.page}>
      <section className={styles.hero}>
        <span>{t("productionHome.eyebrow")}</span>
        <h1>{t("productionHome.title")}</h1>
        <p>{t("productionHome.intro")}</p>
        <Link className={styles.primaryAction} to="/purchases/new">
          <Camera aria-hidden="true" />
          {t("common.captureReceipt")}
        </Link>
      </section>

      <HelpWelcomeCard />

      <Surface as="section" className={styles.recognitionCard} aria-labelledby="recognition-heading">
        <span className={available ? styles.readyIcon : styles.neutralIcon} aria-hidden="true"><ReceiptText /></span>
        <div>
          <h2 id="recognition-heading">{t("productionHome.recognitionTitle")}</h2>
          <p>{recognitionCopy}</p>
          {available && recognition && <small>{recognition.mode}{recognition.models.length > 0 ? ` · ${recognition.models.join(" → ")}` : ""}</small>}
        </div>
      </Surface>

      {/*
        Only receipts that are still in flight or still waiting for the user stay
        on Home. A receipt whose purchase is already saved belongs to the feed
        below, so it is no longer repeated here.
      */}
      {openReceipts.length > 0 && (
        <section className={styles.jobs} aria-labelledby="receipt-jobs-heading">
          <div className={styles.sectionHeading}><h2 id="receipt-jobs-heading">{t("receiptJobs.openTitle")}</h2></div>
          <div className={styles.jobList}>{openReceipts.slice(0, 5).map((receipt) => {
            // Deliberately `warnings`, not `issues`: a receipt in progress also
            // needs a look when only its store, date or currency went
            // unrecognized, and those never become structured issues.
            const needsAttention = receipt.status === "ready" && receipt.warnings.length > 0;
            return (
              <Link className={styles.jobCard} key={receipt.id} to={`/receipts/${receipt.id}`}>
                <ReceiptText aria-hidden="true" />
                <span><strong>{receipt.snapshot?.merchantName ?? t("receiptJobs.receipt")}</strong><small>{t("receiptJobs.parts", { count: receipt.imageParts.length })}</small></span>
                <ReceiptJobStatus status={receipt.status} needsAttention={needsAttention} />
              </Link>
            );
          })}</div>
        </section>
      )}

      {receiptsFailed && <Surface className={styles.jobEmpty} role="alert">{t("receiptJobs.loadFailed")}</Surface>}

      <UnratedPurchasesBlock locale={locale} />

      <PurchaseFeedSection />

      <section className={styles.shortcuts} aria-labelledby="shortcuts-heading">
        <h2 id="shortcuts-heading">{t("productionHome.shortcuts")}</h2>
        <div className={styles.shortcutGrid}>
          <Surface as="article" className={styles.shortcutCard}>
            <History aria-hidden="true" />
            <h3>{t("common.purchases")}</h3>
            <p>{t("productionHome.purchasesDescription")}</p>
            <Link to="/purchases">{t("productionHome.openPurchases")}</Link>
          </Surface>
          <Surface as="article" className={styles.shortcutCard}>
            <span className={styles.attentionIcon} aria-hidden="true"><AlertTriangle /></span>
            <h3>{t("common.review")}</h3>
            <p>{t("productionHome.reviewDescription")}</p>
            <Link to="/needs-attention">{t("productionHome.openReview")}</Link>
          </Surface>
        </div>
      </section>
    </div>
  );
}

/**
 * The status column of one "receipt in progress" card: a step progress bar
 * that fills in as polling moves the receipt through its pipeline, plus the
 * status label itself. The label briefly pulses the moment a receipt lands
 * on "ready" so the polling update reads as a completion, not a text swap.
 */
function ReceiptJobStatus({ status, needsAttention }: { status: ReceiptStatus; needsAttention: boolean }) {
  const { t } = useTranslation();
  const label = needsAttention ? t("status.needsAttention") : t(`status.${status === "queued" ? "processing" : status}`);
  const step = progressStep(status);
  const previousStatus = useRef(status);
  const [justCompleted, setJustCompleted] = useState(false);

  useEffect(() => {
    const wasInFlight = previousStatus.current !== "ready" && previousStatus.current !== "failed";
    if (wasInFlight && status === "ready" && !needsAttention) {
      setJustCompleted(true);
      const timer = setTimeout(() => setJustCompleted(false), 900);
      previousStatus.current = status;
      return () => clearTimeout(timer);
    }
    previousStatus.current = status;
  }, [status, needsAttention]);

  return (
    <span className={styles.jobStatus}>
      <span className={styles.progressTrack} aria-hidden="true">
        <span
          className={styles.progressFill}
          data-failed={status === "failed" || undefined}
          style={{ width: `${((step + 1) / progressSteps.length) * 100}%` }}
        />
      </span>
      <b className={justCompleted ? styles.jobStatusPulse : undefined}>{label}</b>
    </span>
  );
}
