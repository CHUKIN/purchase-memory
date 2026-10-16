import { IonItem, IonLabel, IonList, IonNote } from "@ionic/react";
import { Receipt } from "lucide-react";
import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import type { ProductCategory, ProductDetail, ProductPriceSeries, ProductTag, TagSuggestionsResponse, TagSummary } from "../../api/types";
import { EmptyState, Footnote, ItemLink, Notice, Page, SectionHeader, type BackTarget } from "../../design-system/components";
import { CategoryChip } from "../categories/CategoryChip";
import { categoryPath } from "../categories/categoryText";
import { ProductClassification, type ClassificationChange } from "../categories/ProductClassification";
import { HelpLink } from "../help/HelpLink";
import { ProductTags } from "../tags/ProductTags";
import { TagChips } from "../tags/TagChips";
import { PriceHistoryChart } from "./PriceHistoryChart";
import { formatDate, formatMoney, unitName, unitShortName } from "./productText";
import { MergeWithSection, ProductReviewBanner, type ProductReviewActions } from "./ProductReviewSection";
import { ReviewFlagChip } from "./ReviewFlagChip";

export interface ProductCardProps {
  product: ProductDetail;
  locale: string;
  /** «‹ Товары»; omitted in stories and tests. */
  back?: BackTarget;
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
  /** The household tag list failed to load: tags stay read-only and the card says why. */
  tagsFailed?: boolean;
  /**
   * Photos, barcodes and «Больше не покупать» (features/product-scan). When
   * given, it replaces the purchase-count line and renders that line itself,
   * so variant B can put it beside the photo.
   */
  media?: (intro: string) => ReactNode;
  /**
   * Merge and «Это другой товар»: enables the «проверить» banner's buttons
   * and «Объединить с…». Without it a flagged product only shows its mark.
   */
  reviewActions?: ProductReviewActions;
  /** More sections at the end, e.g. impressions. */
  children?: ReactNode;
}

/**
 * The page the whole product was built for: what this thing cost last time, and
 * how that price has moved. Then its category, brand and tags, and the lists
 * they lead to.
 *
 * The leading series is the unit the product is most often bought in. Any other
 * unit is shown below it, never folded into the same average — see
 * `ProductUnit` on the backend for why.
 */
export function ProductCard({
  product,
  locale,
  back,
  categories = [],
  onClassify,
  allTags,
  onSetTags,
  onCreateTag,
  onSuggestTags,
  readCurrentTagIds,
  tagsFailed = false,
  media,
  reviewActions,
  children
}: ProductCardProps) {
  const { t } = useTranslation();
  const [primarySeries, ...otherSeries] = product.priceSeries;
  const categoryId = product.categoryId ?? null;
  const editable = Boolean(onClassify) && categories.length > 0;
  const tags = product.tags ?? [];
  const tagsEditable = allTags !== undefined && onSetTags !== undefined && onCreateTag !== undefined;
  const related = [
    ...(categoryId
      ? [
        { to: `/purchases?${new URLSearchParams({ category: categoryId })}`, label: t("filters.purchasesInCategory") },
        { to: `/products?${new URLSearchParams({ category: categoryId })}`, label: t("filters.productsInCategory") }
      ]
      : []),
    ...(product.brand ? [{ to: `/purchases?${new URLSearchParams({ brand: product.brand })}`, label: t("filters.purchasesOfBrand", { brand: product.brand }) }] : []),
    ...tags.map((tag) => ({ to: `/purchases?${new URLSearchParams({ tag: tag.id })}`, label: t("filters.purchasesWithTag", { tag: tag.name }) }))
  ];

  return (
    <Page title={product.name} back={back} end={<HelpLink topic="products" />}>
      {/* First on the screen: the open question comes before photos and barcodes. */}
      {reviewActions && <ProductReviewBanner product={product} categories={categories} actions={reviewActions} />}
      {/* Shopping trips, not receipt lines — the same number the frequency block counts by. */}
      {media
        ? media(t("products.purchaseCount", { count: product.purchaseCount }))
        : <p className="pm-home-intro">{t("products.purchaseCount", { count: product.purchaseCount })}</p>}
      {/* Read-only summary; with editing available the sections below say it once. */}
      {((!editable && (categoryId || product.brand)) || (!tagsEditable && tags.length > 0) || (product.needsReview && !reviewActions)) && (
        <div className="pm-chip-row pm-padded">
          {product.needsReview && !reviewActions && <ReviewFlagChip />}
          {!editable && (categoryId || product.brand) && <CategoryChip label={categoryPath(t, categories, categoryId)} source={product.categorySource ?? null} />}
          {!editable && product.brand && <span className="pm-chip pm-none">{product.brand}</span>}
          {!tagsEditable && <TagChips tags={tags} />}
        </div>
      )}

      {primarySeries
        ? <PrimarySeries series={primarySeries} frequency={product.frequency} locale={locale} />
        : (
          <EmptyState icon={<Receipt />} title={t("products.priceHistoryTitle")} description={t("products.priceHistoryEmpty")} />
        )}

      {otherSeries.length > 0 && (
        <section aria-labelledby="other-units-title">
          <SectionHeader id="other-units-title">{t("products.otherUnitsTitle")}</SectionHeader>
          {otherSeries.map((series) => (
            <section key={`${series.currency}-${series.unit}`} aria-labelledby={`unit-${series.currency}-${series.unit}`}>
              <SectionHeader id={`unit-${series.currency}-${series.unit}`} level={3}>{t("products.perUnit", { unit: unitName(t, series.unit) })}</SectionHeader>
              <PriceFigures series={series} locale={locale} />
              <PriceHistoryChart series={series} locale={locale} />
            </section>
          ))}
        </section>
      )}

      {product.unpricedItemCount > 0 && <Footnote>{t("products.unpricedNote", { count: product.unpricedItemCount })}</Footnote>}

      {editable && onClassify && (
        <ProductClassification
          // Remounting after a save resets the state to what the server now says.
          key={`${categoryId}|${product.categorySource}|${product.brand}|${product.brandSource}`}
          categories={categories}
          categoryId={categoryId}
          categorySource={product.categorySource ?? null}
          brand={product.brand ?? null}
          brandSource={product.brandSource ?? null}
          onSave={onClassify}
          help={<HelpLink topic="categories" />}
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
      {tagsFailed && <Notice tone="danger" role="alert"><p>{t("tags.loadError")}</p></Notice>}

      {related.length > 0 && (
        <nav aria-label={t("filters.relatedLabel")}>
          <SectionHeader>{t("filters.relatedLabel")}</SectionHeader>
          <IonList inset className="pm-after-header">
            {related.map((link, index) => (
              <ItemLink key={link.to} to={link.to} lines={index === related.length - 1 ? "none" : undefined}>
                <IonLabel className="pm-wrap">{link.label}</IonLabel>
              </ItemLink>
            ))}
          </IonList>
        </nav>
      )}

      {product.aliases.length > 0 && (
        <section aria-labelledby="aliases-title">
          <SectionHeader id="aliases-title">{t("products.aliasesTitle")}</SectionHeader>
          <IonList inset className="pm-after-header">
            {product.aliases.map((alias, index) => (
              <IonItem key={`${alias.merchantKey}-${alias.rawText}`} lines={index === product.aliases.length - 1 ? "none" : undefined}>
                <IonLabel className="pm-wrap">
                  <h3>{alias.rawText}</h3>
                  <p>{alias.merchantKey || t("products.aliasUnknownMerchant")}</p>
                </IonLabel>
              </IonItem>
            ))}
          </IonList>
        </section>
      )}

      {reviewActions && <MergeWithSection product={product} actions={reviewActions} />}

      {children}
    </Page>
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
      <section aria-labelledby="last-purchase-title">
        <SectionHeader id="last-purchase-title">{t("products.lastPurchase")}</SectionHeader>
        <IonList inset className="pm-after-header">
          <IonItem lines="none">
            <IonLabel className="pm-wrap">
              <p className="pm-big-price">
                <b>{formatMoney(locale, series.currency, latest.unitPrice)}</b>{" "}
                <span>{t("products.perUnit", { unit: unitName(t, series.unit) })}</span>
              </p>
              <p>
                {latest.merchantName ?? t("products.unknownStore")}
                <span aria-hidden="true"> · </span>
                {formatDate(locale, latest.purchasedAt)}
              </p>
            </IonLabel>
          </IonItem>
        </IonList>
        <PriceFigures series={series} locale={locale} />
      </section>

      {frequency && (
        <section aria-labelledby="frequency-title">
          <SectionHeader id="frequency-title">{t("products.frequencyTitle")}</SectionHeader>
          <IonList inset className="pm-after-header">
            <IonItem lines="none">
              <IonLabel className="pm-wrap">
                <h3>
                  {frequency.averageDaysBetweenPurchases === null
                    ? t("products.frequencyOnce")
                    : t("products.frequencyEvery", { days: Math.round(frequency.averageDaysBetweenPurchases) })}
                </h3>
                <p>{t("products.frequencyRange", { from: formatDate(locale, frequency.firstPurchasedAt), to: formatDate(locale, frequency.lastPurchasedAt) })}</p>
              </IonLabel>
            </IonItem>
          </IonList>
        </section>
      )}

      <section aria-labelledby="price-history-title">
        <SectionHeader id="price-history-title">{t("products.priceHistoryTitle")}</SectionHeader>
        <PriceHistoryChart series={series} locale={locale} />
      </section>
    </>
  );
}

/** Minimum, average and latest, always as text beside whatever the chart draws. */
function PriceFigures({ series, locale }: { series: ProductPriceSeries; locale: string }) {
  const { t } = useTranslation();
  const unit = unitShortName(t, series.unit);
  const row = (label: string, value: number, last = false) => (
    <IonItem lines={last ? "none" : undefined}>
      <IonLabel className="pm-wrap">{label}</IonLabel>
      <IonNote slot="end" className="pm-amount">{formatMoney(locale, series.currency, value)}/{unit}</IonNote>
    </IonItem>
  );
  return (
    <IonList inset>
      {/* The latest price is the big figure above; the average is unweighted (explained in help). */}
      {row(t("products.minimumPrice"), series.minimumPrice)}
      {row(t("products.averagePrice"), series.averagePrice, true)}
    </IonList>
  );
}
