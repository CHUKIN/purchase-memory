import { IonBadge, IonButton, IonLabel, IonList, IonProgressBar } from "@ionic/react";
import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, Camera, CircleCheck, CircleEllipsis, ReceiptText } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { getPurchaseFeed } from "../../api/client";
import type { ReceiptStatus, ReceiptView, RecognitionStatus } from "../../api/types";
import { ButtonLink, Footnote, ItemLink, Notice, Page, SectionHeader, StatusText } from "../../design-system/components";
import { SettingsSheet } from "../../app/SettingsSheet";
import { HelpWelcomeCard } from "../help/HelpWelcomeCard";
import { UnratedPurchasesBlock } from "../impressions/ImpressionSections";
import { PurchaseRow } from "../purchases/PurchaseRow";
import { useReturnState } from "../../app/backNavigation";

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
  /** Items in «Нужно проверить»; null while unknown. */
  reviewCount?: number | null;
}

/**
 * «Главная»: one big «Сфотографировать чек», then only what needs the user
 * now — receipts in flight, the review queue, recent lines to rate — and the
 * last few purchases. Help, language and sign-out sit behind «Ещё».
 */
export function HomePage({ recognition, recognitionFailed = false, receipts, receiptsFailed = false, reviewCount = null }: HomePageProps) {
  const { t, i18n } = useTranslation();
  const returnState = useReturnState();
  const locale = i18n.resolvedLanguage === "en" ? "en-GB" : "ru-RU";
  const [settingsOpen, setSettingsOpen] = useState(false);
  const available = recognition?.available === true && !recognitionFailed;
  // Only receipts still in flight or still waiting for the user stay here; a
  // saved one belongs to the purchases below.
  const openReceipts = receipts.filter((receipt) => receipt.status !== "saved");
  const recognitionCopy = recognition === null && !recognitionFailed
    ? t("productionHome.recognitionLoading")
    : available
      ? t("productionHome.recognitionReady")
      : t("productionHome.recognitionOffline");
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
      <p className="pm-home-intro">{t("productionHome.intro")}</p>
      <div className="pm-primary-block">
        <ButtonLink to="/purchases/new" state={returnState} expand="block" shape="round" size="large">
          <Camera aria-hidden="true" size={22} />&nbsp;{t("common.captureReceipt")}
        </ButtonLink>
      </div>
      <Footnote tone={available || (recognition === null && !recognitionFailed) ? "neutral" : "warning"} role="status">
        {recognitionCopy}
      </Footnote>

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

      <IonList inset>
        <ItemLink to="/needs-attention" state={returnState}>
          {reviewCount ? <AlertTriangle slot="start" size={22} className="pm-row-icon pm-row-icon-warning" aria-hidden="true" /> : <CircleCheck slot="start" size={22} className="pm-row-icon" aria-hidden="true" />}
          <IonLabel className="pm-wrap">
            <h2>{t("common.review")}</h2>
            <p>{reviewCount ? t("reviewQueue.summary", { count: reviewCount }) : t("reviewQueue.emptyTitle")}</p>
          </IonLabel>
          {reviewCount ? <IonBadge slot="end" color="danger">{reviewCount > 99 ? "99+" : reviewCount}</IonBadge> : null}
        </ItemLink>
      </IonList>

      <UnratedPurchasesBlock locale={locale} />

      <SectionHeader action={<ButtonLink to="/purchases" fill="clear" size="small">{t("common.viewAll")}</ButtonLink>}>
        {t("common.recentPurchases")}
      </SectionHeader>
      {recent.isError && <Notice tone="danger" role="alert"><p>{t("feed.loadError")}</p></Notice>}
      {recent.data && recent.data.items.length === 0 && <Footnote>{t("feed.emptyDescription")}</Footnote>}
      {recent.data && recent.data.items.length > 0 && (
        <IonList inset className="pm-after-header">
          {recent.data.items.map((purchase) => <PurchaseRow key={purchase.id} purchase={purchase} />)}
        </IonList>
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
        <p>{t("receiptJobs.parts", { count: receipt.imageParts.length })}</p>
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
