import { ArrowLeft, Receipt } from "lucide-react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { ProductCategory, ProductDetail, ProductPriceSeries, ProductTag, TagSuggestionsResponse, TagSummary } from "../../api/types";
import { EmptyState, Surface } from "../../design-system/components";
import { CategoryChip } from "../categories/CategoryChip";
import { categoryPath } from "../categories/categoryText";
import { ProductClassification, type ClassificationChange } from "../categories/ProductClassification";
import { ProductTags } from "../tags/ProductTags";
import { TagChips } from "../tags/TagChips";
import { PriceHistoryChart } from "./PriceHistoryChart";
import { formatDate, formatMoney, unitName, unitShortName } from "./productText";
import styles from "./ProductCard.module.css";

export interface ProductCardProps {
  product: ProductDetail;
  locale: string;
  /** The category tree; without it the card only shows what it already knows. */
  categories?: ProductCategory[];
  /** Saves a person's category/brand decision; enables editing. */
  onClassify?: (change: ClassificationChange) => Promise<unknown>;
  /** Household tags to pick from; with the two callbacks below it enables tag editing. */
  allTags?: TagSummary[];
  /** Replaces the product's tags with exactly these ids. */
  onSetTags?: (tagIds: string[]) => Promise<unknown>;
  onCreateTag?: (name: string) => Promise<ProductTag>;
  /** AI hints from the household's existing tags; adds «Предложить теги». */
  onSuggestTags?: () => Promise<TagSuggestionsResponse>;
  /** The product's tags as the cache knows them right now; accepting builds on these. */
  readCurrentTagIds?: () => string[] | undefined;
}

/**
 * The page the whole product was built for: what this thing cost last time, and
 * how that price has moved.
 *
 * The leading series is the unit the product is most often bought in. Any other
 * unit is shown below it, never folded into the same average — see
 * `ProductUnit` on the backend for why.
 */
export function ProductCard({
  product,
  locale,
  categories = [],
  onClassify,
  allTags,
  onSetTags,
  onCreateTag,
  onSuggestTags,
  readCurrentTagIds
}: ProductCardProps) {
  const { t } = useTranslation();
  const [primarySeries, ...otherSeries] = product.priceSeries;
  const categoryId = product.categoryId ?? null;
  const editable = Boolean(onClassify) && categories.length > 0;
  const tags = product.tags ?? [];
  const tagsEditable = allTags !== undefined && onSetTags !== undefined && onCreateTag !== undefined;

  return (
    <article className={styles.card}>
      <header className={styles.header}>
        <Link className={styles.back} to="/products">
          <ArrowLeft aria-hidden="true" />
          {t("products.backToCatalog")}
        </Link>
        <h1>{product.name}</h1>
        <p className={styles.meta}>
          {/* Shopping trips, not receipt lines — the same number the frequency
              block below counts by, so the two can never disagree. */}
          {t("products.purchaseCount", { count: product.purchaseCount })}
        </p>
        {/* Read-only summary; with editing available the section below says it once. */}
        {!editable && (categoryId || product.brand) && (
          <div className={styles.classification}>
            <CategoryChip label={categoryPath(t, categories, categoryId)} source={product.categorySource ?? null} />
            {product.brand && <span className={styles.brand}>{product.brand}</span>}
          </div>
        )}
        {!tagsEditable && <TagChips tags={tags} />}
      </header>

      {editable && onClassify && (
        <ProductClassification
          // Remounting after a save resets the draft to what the server now says.
          key={`${categoryId}|${product.categorySource}|${product.brand}|${product.brandSource}`}
          categories={categories}
          categoryId={categoryId}
          categorySource={product.categorySource ?? null}
          brand={product.brand ?? null}
          brandSource={product.brandSource ?? null}
          onSave={onClassify}
        />
      )}

      {tagsEditable && (
        <ProductTags
          // Keyed by the product, not by its tag list: another product starts
          // fresh (no stale suggestions or drafts), while accepting a suggestion
          // keeps the remaining ones. Editing re-reads the tags when it starts.
          key={product.id}
          tags={tags}
          allTags={allTags}
          onSave={onSetTags}
          onCreate={onCreateTag}
          onSuggest={onSuggestTags}
          readCurrentTagIds={readCurrentTagIds}
        />
      )}

      {(categoryId || product.brand || tags.length > 0) && (
        <nav className={styles.filterLinks} aria-label={t("filters.relatedLabel")}>
          {categoryId && (
            <>
              <Link to={`/purchases?${new URLSearchParams({ category: categoryId })}`}>{t("filters.purchasesInCategory")}</Link>
              <Link to={`/products?${new URLSearchParams({ category: categoryId })}`}>{t("filters.productsInCategory")}</Link>
            </>
          )}
          {product.brand && (
            <Link to={`/purchases?${new URLSearchParams({ brand: product.brand })}`}>
              {t("filters.purchasesOfBrand", { brand: product.brand })}
            </Link>
          )}
          {tags.map((tag) => (
            <Link key={tag.id} to={`/purchases?${new URLSearchParams({ tag: tag.id })}`}>
              {t("filters.purchasesWithTag", { tag: tag.name })}
            </Link>
          ))}
        </nav>
      )}

      {primarySeries
        ? <PrimarySeries series={primarySeries} frequency={product.frequency} locale={locale} />
        : (
          <Surface className={styles.section}>
            {/* Sits where the other section headings sit, so it has to be an
                h2: an h3 straight after the product's h1 skips a level. */}
            <EmptyState
              icon={<Receipt />}
              title={t("products.priceHistoryTitle")}
              description={t("products.priceHistoryEmpty")}
              headingLevel={2}
            />
          </Surface>
        )}

      {otherSeries.length > 0 && (
        <Surface as="section" className={styles.section}>
          <h2>{t("products.otherUnitsTitle")}</h2>
          <p className={styles.note}>{t("products.otherUnitsNote")}</p>
          <div className={styles.otherSeries}>
            {otherSeries.map((series) => (
              <section className={styles.otherSeriesEntry} key={`${series.currency}-${series.unit}`}>
                <h3>{t("products.perUnit", { unit: unitName(t, series.unit) })}</h3>
                <PriceFigures series={series} locale={locale} />
                <PriceHistoryChart series={series} locale={locale} />
              </section>
            ))}
          </div>
        </Surface>
      )}

      {product.unpricedItemCount > 0 && (
        <p className={styles.note}>{t("products.unpricedNote", { count: product.unpricedItemCount })}</p>
      )}

      {product.aliases.length > 0 && (
        <Surface as="section" className={styles.section}>
          <h2>{t("products.aliasesTitle")}</h2>
          <ul className={styles.aliases}>
            {product.aliases.map((alias) => (
              <li key={`${alias.merchantKey}-${alias.rawText}`}>
                <span className={styles.aliasStore}>{alias.merchantKey || t("products.aliasUnknownMerchant")}</span>
                <span>{alias.rawText}</span>
              </li>
            ))}
          </ul>
        </Surface>
      )}
    </article>
  );
}

function PrimarySeries({
  series,
  frequency,
  locale
}: {
  series: ProductPriceSeries;
  frequency: ProductDetail["frequency"];
  locale: string;
}) {
  const { t } = useTranslation();
  const latest = series.latestPoint;

  return (
    <>
      <Surface as="section" className={styles.section}>
        <h2>{t("products.lastPurchase")}</h2>
        <p className={styles.lastPurchase}>
          <b>{formatMoney(locale, series.currency, latest.unitPrice)}</b>
          <span className={styles.perUnit}>{t("products.perUnit", { unit: unitName(t, series.unit) })}</span>
        </p>
        <p className={styles.meta}>
          {latest.merchantName ?? t("products.unknownStore")}
          <span aria-hidden="true"> · </span>
          {formatDate(locale, latest.purchasedAt)}
        </p>
        <PriceFigures series={series} locale={locale} />
      </Surface>

      {frequency && (
        <Surface as="section" className={styles.section}>
          <h2>{t("products.frequencyTitle")}</h2>
          <p className={styles.frequency}>
            {frequency.averageDaysBetweenPurchases === null
              ? t("products.frequencyOnce")
              : t("products.frequencyEvery", { days: Math.round(frequency.averageDaysBetweenPurchases) })}
          </p>
          <p className={styles.meta}>
            {t("products.frequencyRange", {
              from: formatDate(locale, frequency.firstPurchasedAt),
              to: formatDate(locale, frequency.lastPurchasedAt)
            })}
          </p>
        </Surface>
      )}

      <Surface as="section" className={styles.section}>
        <h2>{t("products.priceHistoryTitle")}</h2>
        <PriceHistoryChart series={series} locale={locale} />
      </Surface>
    </>
  );
}

/** Minimum, average and latest, always as text beside whatever the chart draws. */
function PriceFigures({ series, locale }: { series: ProductPriceSeries; locale: string }) {
  const { t } = useTranslation();
  const unit = unitShortName(t, series.unit);

  return (
    <dl className={styles.figures}>
      <div>
        <dt>{t("products.lastPrice")}</dt>
        <dd>{formatMoney(locale, series.currency, series.latestPrice)}<span className={styles.unit}>/{unit}</span></dd>
      </div>
      <div>
        <dt>{t("products.minimumPrice")}</dt>
        <dd>{formatMoney(locale, series.currency, series.minimumPrice)}<span className={styles.unit}>/{unit}</span></dd>
      </div>
      <div>
        {/* The average is unweighted: a 5 kg purchase and a 200 g one count the
            same. Saying so here keeps the number from reading as something it
            is not. */}
        <dt>{t("products.averagePrice")}</dt>
        <dd>
          {formatMoney(locale, series.currency, series.averagePrice)}
          <span className={styles.unit}>/{unit}</span>
          <span className={styles.figureHint}>{t("products.averagePriceHint")}</span>
        </dd>
      </div>
    </dl>
  );
}
