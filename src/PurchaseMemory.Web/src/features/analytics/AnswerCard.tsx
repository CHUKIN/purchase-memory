import { IonItem, IonLabel, IonList, IonNote } from "@ionic/react";
import { Info, ReceiptText } from "lucide-react";
import { forwardRef } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { AnalyticsAnswer, AnalyticsPlan, ProductCategory, ProductTag } from "../../api/types";
import { Footnote, SectionHeader } from "../../design-system/components";
import { formatDate, formatMoney } from "../products/productText";
import { AnalyticsChart } from "./AnalyticsChart";
import { coverageLines, kpiLabel, kpiValue, noteText, productNamesOf } from "./analyticsText";
import { PlanFilters } from "./PlanFilters";
import { useReturnState } from "../../app/backNavigation";

export interface AnswerCardProps {
  answer: AnalyticsAnswer;
  categories: ProductCategory[];
  merchants: string[];
  /** The household's tags, for tag chips and the tag field. */
  tags?: ProductTag[];
  locale: string;
  onPlanChange: (plan: AnalyticsPlan) => void;
  /** An edited plan is running; the card keeps showing the previous numbers meanwhile. */
  rerunning?: boolean;
}

/**
 * One answer: the conclusion, headline numbers, the chart, the interpretation
 * as editable chips, what the numbers rest on, and the receipts themselves.
 * The ref goes to the conclusion heading, which the page focuses when a new
 * answer arrives.
 */
export const AnswerCard = forwardRef<HTMLHeadingElement, AnswerCardProps>(function AnswerCard(
  { answer, categories, merchants, tags = [], locale, onPlanChange, rerunning = false },
  conclusionRef
) {
  const { t } = useTranslation();
  const returnState = useReturnState();
  const { result } = answer;
  const priceUnit = result.kpis.find((kpi) => kpi.code === "priceCount")?.unit ?? null;
  const kpis = result.kpis.filter((kpi) => kpi.code !== "priceCount");

  return (
    <article aria-labelledby="analytics-conclusion" aria-busy={rerunning || undefined} className="pm-answer">
      <div className="pm-answer-card">
        <h2 id="analytics-conclusion" ref={conclusionRef} tabIndex={-1} className="pm-conclusion">{answer.conclusion}</h2>
      </div>

      {result.hasData && kpis.length > 0 && (
        <IonList inset>
          {kpis.map((kpi, index) => (
            <IonItem key={kpi.code} lines={index === kpis.length - 1 ? "none" : undefined}>
              <IonLabel className="pm-wrap">{kpiLabel(t, kpi, priceUnit)}</IonLabel>
              <IonNote slot="end" className="pm-amount pm-kpi">{kpiValue(t, locale, kpi, result.currency)}</IonNote>
            </IonItem>
          ))}
        </IonList>
      )}

      {result.hasData && <AnalyticsChart answer={answer} categories={categories} locale={locale} />}

      <PlanFilters
        plan={answer.plan}
        categories={categories}
        merchants={merchants}
        tags={tags}
        productNames={productNamesOf(answer)}
        locale={locale}
        onChange={onPlanChange}
        busy={rerunning}
      />

      {answer.notes.length > 0 && (
        <ul className="pm-notes" aria-label={t("analytics.notes.title")}>
          {answer.notes.map((note, index) => (
            <li key={`${note.code}-${index}`}><Info aria-hidden="true" size={15} />{noteText(t, note)}</li>
          ))}
        </ul>
      )}

      <section aria-labelledby="analytics-coverage-title">
        <SectionHeader id="analytics-coverage-title" level={3}>{t("analytics.coverage.title")}</SectionHeader>
        <IonList inset className="pm-after-header">
          {coverageLines(t, result.coverage, answer.plan).map((line, index, lines) => (
            <IonItem key={line} lines={index === lines.length - 1 ? "none" : undefined}>
              <IonLabel className="pm-wrap"><p className="pm-plain-text">{line}</p></IonLabel>
            </IonItem>
          ))}
        </IonList>
        {!result.hasData && <Footnote>{t("analytics.noDataHint")}</Footnote>}
      </section>

      {result.evidence.length > 0 && (
        <section aria-labelledby="analytics-evidence-title">
          <SectionHeader id="analytics-evidence-title" level={3}>{t("analytics.evidence.title")}</SectionHeader>
          {result.evidenceTotal > result.evidence.length && (
            <Footnote>{t("analytics.evidence.shown", { shown: result.evidence.length, count: result.evidenceTotal })}</Footnote>
          )}
          {result.evidence.map((purchase) => {
            const heading = `${formatDate(locale, purchase.purchasedAt)} · ${purchase.merchantName ?? t("analytics.groups.noMerchant")}`;
            return (
              <IonList inset key={purchase.purchaseId}>
                <IonItem>
                  <ReceiptText slot="start" size={18} className="pm-row-icon" aria-hidden="true" />
                  <IonLabel className="pm-wrap">
                    <h3>
                      {purchase.sourceReceiptId
                        ? <Link className="pm-text-link" to={`/receipts/${purchase.sourceReceiptId}`} state={returnState}>{heading}</Link>
                        : heading}
                    </h3>
                  </IonLabel>
                  <IonNote slot="end" className="pm-amount">{formatMoney(locale, purchase.currency, purchase.amount)}</IonNote>
                </IonItem>
                {purchase.items.map((item, index) => (
                  <IonItem key={`${item.name}-${index}`} lines={index === purchase.items.length - 1 && purchase.matchedLineCount <= purchase.items.length ? "none" : undefined}>
                    <IonLabel className="pm-wrap">
                      <p className="pm-plain-text">
                        {item.productId ? <Link className="pm-text-link pm-primary-text" to={`/products/${item.productId}`} state={returnState}>{item.name}</Link> : <span>{item.name}</span>}
                      </p>
                      {item.discountTotal !== 0 && (
                        <p>{t("analytics.evidence.discount", { amount: formatMoney(locale, purchase.currency, item.discountTotal) })}</p>
                      )}
                    </IonLabel>
                  </IonItem>
                ))}
                {purchase.matchedLineCount > purchase.items.length && (
                  <IonItem lines="none">
                    <IonLabel color="medium"><p>{t("analytics.evidence.moreItems", { count: purchase.matchedLineCount - purchase.items.length })}</p></IonLabel>
                  </IonItem>
                )}
              </IonList>
            );
          })}
        </section>
      )}
    </article>
  );
});
