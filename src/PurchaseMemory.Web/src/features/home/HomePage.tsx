import { IonButton, IonLabel, IonList, IonProgressBar } from "@ionic/react";
import { useQuery } from "@tanstack/react-query";
import { CircleEllipsis, ReceiptText } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { getPurchaseFeed } from "../../api/client";
import type { ReceiptStatus, ReceiptView, RecognitionStatus } from "../../api/types";
import { ButtonLink, Footnote, ItemLink, Notice, Page, SectionHeader, StatusText } from "../../design-system/components";
import { SettingsSheet } from "../../app/SettingsSheet";
import { HelpWelcomeCard } from "../help/HelpWelcomeCard";
import { HomeCaptureActions } from "../product-scan/HomeCaptureActions";
import { UnratedPurchasesBlock } from "../impressions/ImpressionSections";
import { PurchaseCards, PurchaseRow } from "../purchases/PurchaseRow";
import { useReturnState } from "../../app/backNavigation";
import { HomeReviewCard } from "./HomeReviewCard";

// uploaded -> queued -> processing -> ready/failed, used to drive the
// progress bar of a receipt still in flight.
const progressSteps: ReceiptStatus[] = ["uploaded", "queued", "processing", "ready"];

function progressStep(status: ReceiptStatus): number {
  const index = progressSteps.indexOf(status);
  return index === -1 ? progressSteps.length - 1 : index; // "failed" also reads as the final step
}

const recentPurchaseCount = 5;

export interface HomePageProps {
  recognition: RecognitionStatus | null;
  recognitionFailed?: boolean;
  receipts: ReceiptView[];
  receiptsFailed?: boolean;
  /** Everything waiting in «Ревью» (see useReviewCount); null while unknown. */
  reviewCount?: number | null;
}

/**
 * «Главная»: «Сфотографировать чек» and «Сканировать» (two looks, see
 * product-scan/scanVariant.ts) with the «Ревью» card under them, then only
 * what needs the user now — receipts in flight, recent lines to rate — and the
 * last few purchases. Help and sign-out sit behind «Ещё».
 */
export function HomePage({ recognition, recognitionFailed = false, receipts, receiptsFailed = false, reviewCount = null }: HomePageProps) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage === "en" ? "en-GB" : "ru-RU";
  const [settingsOpen, setSettingsOpen] = useState(false);
  const available = recognition?.available === true && !recognitionFailed;
  // Only receipts still in flight or still waiting for the user stay here; a
  // saved one belongs to the purchases below.
  const openReceipts = receipts.filter((receipt) => receipt.status !== "saved");
  // Only a problem is worth a line here; «ready» is the normal state.
  const recognitionDown = !available && (recognition !== null || recognitionFailed);
  const recent = useQuery({
    queryKey: ["purchases", "feed", "home", recentPurchaseCount],
    queryFn: () => getPurchaseFeed({ page: 1, pageSize: recentPurchaseCount })
  });

  return (
    <Page
      title={t("common.home")}
      end={(
        <IonButton className="pm-icon-button" onClick={() => setSettingsOpen(true)} aria-label={t("settings.open")}>
          <CircleEllipsis size={26} aria-hidden="true" />
        </IonButton>
      )}
    >
      <HomeCaptureActions />
      <HomeReviewCard count={reviewCount} />
      <div role="status">
        {recognitionDown && <Footnote tone="warning">{t("productionHome.recognitionOffline")}</Footnote>}
      </div>

      <HelpWelcomeCard />

      {openReceipts.length > 0 && (
        <>
          <SectionHeader>{t("receiptJobs.openTitle")}</SectionHeader>
          <IonList inset className="pm-after-header">
            {openReceipts.slice(0, 5).map((receipt) => (
              <ReceiptJobRow key={receipt.id} receipt={receipt} />
            ))}
          </IonList>
        </>
      )}
      {receiptsFailed && <Notice tone="danger" role="alert"><p>{t("receiptJobs.loadFailed")}</p></Notice>}

      <UnratedPurchasesBlock locale={locale} />

      <SectionHeader variant="title" action={<ButtonLink to="/purchases" fill="clear" size="small">{t("common.viewAll")}</ButtonLink>}>
        {t("common.recentPurchases")}
      </SectionHeader>
      {recent.isError && <Notice tone="danger" role="alert"><p>{t("feed.loadError")}</p></Notice>}
      {recent.data && recent.data.items.length === 0 && <Footnote>{t("feed.emptyTitle")}</Footnote>}
      {recent.data && recent.data.items.length > 0 && (
        <PurchaseCards>
          {recent.data.items.map((purchase) => <PurchaseRow key={purchase.id} purchase={purchase} />)}
        </PurchaseCards>
      )}

      <SettingsSheet isOpen={settingsOpen} onClose={() => setSettingsOpen(false)} recognition={recognition} recognitionFailed={recognitionFailed} />
    </Page>
  );
}

/**
 * One receipt in flight: store (once known), number of photos, its status and
 * a progress bar that fills as polling moves it through the pipeline. The
 * status briefly pulses the moment it lands on «ready».
 */
function ReceiptJobRow({ receipt }: { receipt: ReceiptView }) {
  const { t } = useTranslation();
  const returnState = useReturnState();
  // Deliberately `warnings`, not `issues`: a receipt in progress also needs a
  // look when only its store, date or currency went unrecognized.
  const needsAttention = receipt.status === "ready" && receipt.warnings.length > 0;
  const status = receipt.status;
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

  const label = needsAttention ? "needsAttention" : status === "queued" ? "processing" : status;
  return (
    <ItemLink to={`/receipts/${receipt.id}`} state={returnState}>
      <ReceiptText slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
      <IonLabel className="pm-wrap">
        <h2>{receipt.snapshot?.merchantName ?? t("receiptJobs.receipt")}</h2>
        {/* One photo is the usual case; only a long receipt in parts says so. */}
        {receipt.imageParts.length > 1 && <p>{t("receiptJobs.parts", { count: receipt.imageParts.length })}</p>}
        <p className={justCompleted ? "pm-pulse" : undefined}><StatusText status={label} /></p>
        <IonProgressBar
          className="pm-job-progress"
          color={status === "failed" ? "danger" : "primary"}
          value={(step + 1) / progressSteps.length}
          aria-hidden="true"
        />
      </IonLabel>
    </ItemLink>
  );
}
