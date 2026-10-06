import { Info, ReceiptText } from "lucide-react";
import { forwardRef } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { AnalyticsAnswer, AnalyticsPlan, ProductCategory, ProductTag } from "../../api/types";
import { Surface } from "../../design-system/components";
import { formatDate, formatMoney } from "../products/productText";
import { AnalyticsChart } from "./AnalyticsChart";
import { coverageLines, kpiLabel, kpiValue, noteText, productNamesOf } from "./analyticsText";
import { PlanFilters } from "./PlanFilters";
import styles from "./AnswerCard.module.css";

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
  const { result } = answer;
  const priceUnit = result.kpis.find((kpi) => kpi.code === "priceCount")?.unit ?? null;
  const kpis = result.kpis.filter((kpi) => kpi.code !== "priceCount");

  return (
    <Surface as="article" className={styles.card} aria-labelledby="analytics-conclusion" aria-busy={rerunning || undefined}>
      <h2 id="analytics-conclusion" ref={conclusionRef} tabIndex={-1} className={styles.conclusion}>
        {answer.conclusion}
      </h2>

      {result.hasData && kpis.length > 0 && (
        <dl className={styles.kpis}>
          {kpis.map((kpi) => (
            <div key={kpi.code} className={styles.kpi}>
              <dt>{kpiLabel(t, kpi, priceUnit)}</dt>
              <dd>{kpiValue(t, locale, kpi, result.currency)}</dd>
            </div>
          ))}
        </dl>
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
        <ul className={styles.notes} aria-label={t("analytics.notes.title")}>
          {answer.notes.map((note, index) => (
            <li key={`${note.code}-${index}`}><Info aria-hidden="true" size={15} />{noteText(t, note)}</li>
          ))}
        </ul>
      )}

      <section className={styles.coverage} aria-labelledby="analytics-coverage-title">
        <h3 id="analytics-coverage-title">{t("analytics.coverage.title")}</h3>
        <ul>
          {coverageLines(t, result.coverage, answer.plan).map((line) => <li key={line}>{line}</li>)}
        </ul>
        {!result.hasData && <p className={styles.hint}>{t("analytics.noDataHint")}</p>}
      </section>

      {result.evidence.length > 0 && (
        <section className={styles.evidence} aria-labelledby="analytics-evidence-title">
          <h3 id="analytics-evidence-title">{t("analytics.evidence.title")}</h3>
          {result.evidenceTotal > result.evidence.length && (
            <p className={styles.hint}>{t("analytics.evidence.shown", { shown: result.evidence.length, count: result.evidenceTotal })}</p>
          )}
          <ul className={styles.purchases}>
            {result.evidence.map((purchase) => {
              const heading = `${formatDate(locale, purchase.purchasedAt)} · ${purchase.merchantName ?? t("analytics.groups.noMerchant")}`;
              return (
                <li key={purchase.purchaseId} className={styles.purchase}>
                  <div className={styles.purchaseHeader}>
                    {purchase.sourceReceiptId ? (
                      <Link to={`/receipts/${purchase.sourceReceiptId}`} className={styles.purchaseLink}>
                        <ReceiptText aria-hidden="true" size={16} />{heading}
                      </Link>
                    ) : (
                      <span className={styles.purchaseLink}><ReceiptText aria-hidden="true" size={16} />{heading}</span>
                    )}
                    <span className={styles.amount}>{formatMoney(locale, purchase.currency, purchase.amount)}</span>
                  </div>
                  <ul className={styles.items}>
                    {purchase.items.map((item, index) => (
                      <li key={`${item.name}-${index}`}>
                        {item.productId
                          ? <Link to={`/products/${item.productId}`}>{item.name}</Link>
                          : <span>{item.name}</span>}
                        {item.discountTotal !== 0 && (
                          <span className={styles.discount}>{t("analytics.evidence.discount", { amount: formatMoney(locale, purchase.currency, item.discountTotal) })}</span>
                        )}
                      </li>
                    ))}
                    {purchase.matchedLineCount > purchase.items.length && (
                      <li className={styles.hint}>{t("analytics.evidence.moreItems", { count: purchase.matchedLineCount - purchase.items.length })}</li>
                    )}
                  </ul>
                </li>
              );
            })}
          </ul>
        </section>
      )}
    </Surface>
  );
});
