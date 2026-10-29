import { AlertTriangle, ChevronRight, CircleCheck } from "lucide-react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { useReturnState } from "../../app/backNavigation";
import { badgeText } from "../../app/AppShell/AppShell";
import { reviewQueuePath } from "../review-queue/returnToQueue";

/**
 * «Ревью» on Главная, right under the capture tiles: one card that opens the
 * hub with everything to check or do. With something waiting it is warm
 * (⚠ and the count, the same number as the tab badge); with nothing it says
 * «Всё проверено»; while the count is unknown it is a plain card.
 */
export function HomeReviewCard({ count }: { count: number | null }) {
  const { t } = useTranslation();
  const returnState = useReturnState();
  const pending = count !== null && count > 0;
  // The badge is decoration; the name says the count as a sentence.
  const label = pending
    ? `${t("common.review")}, ${t("reviewQueue.summary", { count })}`
    : count === 0 ? `${t("common.review")}, ${t("reviewQueue.emptyTitle")}` : t("common.review");
  return (
    <Link to={reviewQueuePath} state={returnState} className="pm-review-card" aria-label={label} data-tone={pending ? "warning" : undefined}>
      {pending
        ? <AlertTriangle size={22} className="pm-review-card-icon" aria-hidden="true" />
        : <CircleCheck size={22} className="pm-review-card-icon" aria-hidden="true" />}
      <span className="pm-review-card-text">
        <span className="pm-review-card-title">{t("common.review")}</span>
        {count === 0 && <span className="pm-review-card-note">{t("reviewQueue.emptyTitle")}</span>}
      </span>
      {pending && <span className="pm-review-card-badge" aria-hidden="true">{badgeText(count)}</span>}
      <ChevronRight size={20} className="pm-review-card-chevron" aria-hidden="true" />
    </Link>
  );
}
