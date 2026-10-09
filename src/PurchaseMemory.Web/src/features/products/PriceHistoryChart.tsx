import { lazy, Suspense } from "react";
import { useTranslation } from "react-i18next";
import type { ProductPriceSeries } from "../../api/types";
import { fallbackCurrency, formatDate, formatMoney, unitName, unitShortName } from "./productText";
import styles from "./PriceHistoryChart.module.css";

// The charting library is the heaviest dependency in the app and only this page
// needs it, so it is fetched when a product is actually opened. The table below
// carries the same numbers and never waits for it.
const PriceLineChart = lazy(() => import("./PriceLineChart"));

export interface PriceHistoryChartProps {
  series: ProductPriceSeries;
  locale: string;
}

/**
 * The price over time, drawn and then repeated as a table.
 *
 * The table is not a fallback — it is the accessible copy of the same data, and
 * it stays in the DOM whether or not the chart renders. The chart itself is
 * hidden from assistive technology, because an SVG of line segments has nothing
 * to say that the rows below do not say better.
 */
export function PriceHistoryChart({ series, locale }: PriceHistoryChartProps) {
  const { t } = useTranslation();
  const currency = series.currency ?? fallbackCurrency;
  const unit = unitShortName(t, series.unit);

  const data = series.points.map((point) => ({
    date: formatDate(locale, point.purchasedAt),
    price: point.unitPrice
  }));

  return (
    <div className={styles.root}>
      {series.points.length > 1 && (
        <div
          className={styles.chart}
          role="img"
          aria-label={t("products.chartLabel", { unit: unitName(t, series.unit), currency })}
        >
          <Suspense fallback={<div className={styles.chartPlaceholder} />}>
            <PriceLineChart data={data} formatPrice={(value) => formatMoney(locale, currency, value)} />
          </Suspense>
        </div>
      )}

      {series.points.length === 1 && <p className={styles.single}>{t("products.priceHistorySingle")}</p>}

      <table className={styles.table}>
        <caption>{t("products.tableCaption")}</caption>
        <thead>
          <tr>
            <th scope="col">{t("products.tableDate")}</th>
            <th scope="col">{t("products.tableStore")}</th>
            <th scope="col">{t("products.tablePrice", { unit })}</th>
          </tr>
        </thead>
        <tbody>
          {[...series.points].reverse().map((point) => (
            <tr key={`${point.purchaseId}-${point.purchasedAt}-${point.unitPrice}`}>
              <td className={styles.date}>{formatDate(locale, point.purchasedAt)}</td>
              <td>{point.merchantName ?? t("products.unknownStore")}</td>
              <td className={styles.price}>{formatMoney(locale, currency, point.unitPrice)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
