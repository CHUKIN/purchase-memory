import { lazy, Suspense } from "react";
import { useTranslation } from "react-i18next";
import type { AnalyticsAnswer, ProductCategory } from "../../api/types";
import { formatGroupValue, groupLabel } from "./analyticsText";
import styles from "./AnalyticsChart.module.css";

const AnalyticsBarChart = lazy(() => import("./AnalyticsBarChart"));

export interface AnalyticsChartProps {
  answer: AnalyticsAnswer;
  categories: ProductCategory[];
  locale: string;
}

/**
 * Groups of the answer as bars, then the same rows as a table. The chart is
 * decoration for sighted readers and hidden from screen readers; the table is
 * the accessible copy and is always rendered.
 */
export function AnalyticsChart({ answer, categories, locale }: AnalyticsChartProps) {
  const { t } = useTranslation();
  const groups = answer.result.groups;
  if (groups.length === 0) return null;

  const timeline = answer.result.groupBy === "month" || answer.result.groupBy === "week";
  const rows = groups.map((group) => ({
    key: group.key,
    label: groupLabel(t, locale, answer, group, categories),
    value: group.value,
    productId: group.productId
  }));
  const format = (value: number) => formatGroupValue(t, locale, answer, value);

  return (
    <section className={styles.root} aria-labelledby="analytics-chart-title">
      <h3 id="analytics-chart-title" className={styles.title}>{t(`analytics.chartTitle.${answer.result.groupBy}`)}</h3>
      {rows.length > 1 && (
        <div className={styles.chart} aria-hidden="true">
          <Suspense fallback={<div className={styles.placeholder} />}>
            <AnalyticsBarChart data={rows} formatValue={format} horizontal={!timeline} />
          </Suspense>
        </div>
      )}
      <table className={styles.table}>
        <caption className="visually-hidden">{t(`analytics.chartTitle.${answer.result.groupBy}`)}</caption>
        <thead>
          <tr>
            <th scope="col">{t(`analytics.groupColumn.${answer.result.groupBy}`)}</th>
            <th scope="col">{t(`analytics.metrics.${answer.result.metric === "top" ? "spend" : answer.result.metric}`)}</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.key}>
              <td>{row.label}</td>
              <td className={styles.value}>{format(row.value)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {answer.result.groupBy === "tag" && (answer.result.coverage.multiTaggedLines ?? 0) > 0 && (
        <p className={styles.more}>{t("analytics.groups.tagOverlap")}</p>
      )}
      {answer.result.hiddenGroupCount > 0 && (
        <p className={styles.more}>{t("analytics.hiddenGroups", { count: answer.result.hiddenGroupCount })}</p>
      )}
    </section>
  );
}
