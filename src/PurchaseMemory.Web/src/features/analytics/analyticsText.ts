import type { TFunction } from "i18next";
import type {
  AnalyticsAnswer,
  AnalyticsCoverage,
  AnalyticsGroup,
  AnalyticsKpi,
  AnalyticsNote,
  AnalyticsPlan,
  ProductCategory,
  ProductTag
} from "../../api/types";
import { categoryLabel, categoryPath } from "../categories/categoryText";
import { formatMoney, unitShortName } from "../products/productText";

/** «2026-08-31» → a calendar day, without the time-zone shift `new Date("2026-08-31")` would add. */
export function formatDay(locale: string, day: string): string {
  const [year, month, date] = day.split("-").map(Number);
  return new Intl.DateTimeFormat(locale, { day: "2-digit", month: "2-digit", year: "numeric" })
    .format(new Date(year, month - 1, date));
}

export function formatNumber(locale: string, value: number): string {
  return new Intl.NumberFormat(locale, { maximumFractionDigits: 3 }).format(value);
}

export function periodLabel(t: TFunction, locale: string, plan: Pick<AnalyticsPlan, "from" | "to">): string {
  if (plan.from && plan.to) return `${formatDay(locale, plan.from)} – ${formatDay(locale, plan.to)}`;
  if (plan.from) return t("analytics.period.since", { day: formatDay(locale, plan.from) });
  if (plan.to) return t("analytics.period.until", { day: formatDay(locale, plan.to) });
  return t("analytics.period.all");
}

export type ChipKind = "metric" | "category" | "term" | "product" | "brand" | "tag" | "merchant" | "allMerchants" | "period" | "discount" | "groupBy";

export interface PlanChip {
  /** Stable React key. */
  key: string;
  kind: ChipKind;
  label: string;
  /** The plan without this filter, or null when the chip cannot be removed. */
  without: AnalyticsPlan | null;
}

/**
 * The interpretation as chips, in reading order: what, which, where, when.
 * Everything that narrows the answer can be removed; the metric and «all
 * stores» only describe it.
 */
export function planChips(
  t: TFunction,
  locale: string,
  plan: AnalyticsPlan,
  categories: ProductCategory[],
  productNames: Map<string, string>,
  tags: ProductTag[] = []
): PlanChip[] {
  const chips: PlanChip[] = [{ key: "metric", kind: "metric", label: t(`analytics.metrics.${plan.metric}`), without: null }];
  for (const id of plan.categoryIds) {
    chips.push({
      key: `category-${id}`,
      kind: "category",
      label: categoryPath(t, categories, id) ?? t("analytics.chips.unknownCategory"),
      without: { ...plan, categoryIds: plan.categoryIds.filter((entry) => entry !== id) }
    });
  }
  for (const term of plan.nameTerms) {
    chips.push({
      key: `term-${term}`,
      kind: "term",
      label: t("analytics.chips.term", { term }),
      without: { ...plan, nameTerms: plan.nameTerms.filter((entry) => entry !== term) }
    });
  }
  for (const id of plan.productIds) {
    chips.push({
      key: `product-${id}`,
      kind: "product",
      label: productNames.get(id) ?? t("analytics.chips.product"),
      without: { ...plan, productIds: plan.productIds.filter((entry) => entry !== id) }
    });
  }
  for (const brand of plan.brands) {
    chips.push({
      key: `brand-${brand}`,
      kind: "brand",
      label: t("analytics.chips.brand", { brand }),
      without: { ...plan, brands: plan.brands.filter((entry) => entry !== brand) }
    });
  }
  const tagIds = plan.tagIds ?? [];
  for (const id of tagIds) {
    const name = tags.find((tag) => tag.id === id)?.name;
    chips.push({
      key: `tag-${id}`,
      kind: "tag",
      label: name ? t("analytics.chips.tag", { tag: name }) : t("analytics.chips.unknownTag"),
      without: { ...plan, tagIds: tagIds.filter((entry) => entry !== id) }
    });
  }
  if (plan.merchants.length === 0) {
    chips.push({ key: "all-merchants", kind: "allMerchants", label: t("analytics.chips.allMerchants"), without: null });
  }
  for (const merchant of plan.merchants) {
    chips.push({
      key: `merchant-${merchant}`,
      kind: "merchant",
      label: merchant,
      without: { ...plan, merchants: plan.merchants.filter((entry) => entry !== merchant) }
    });
  }
  chips.push({
    key: "period",
    kind: "period",
    label: periodLabel(t, locale, plan),
    without: plan.from || plan.to ? { ...plan, from: null, to: null } : null
  });
  if (plan.discount !== "any") {
    chips.push({ key: "discount", kind: "discount", label: t(`analytics.discount.${plan.discount}`), without: { ...plan, discount: "any" } });
  }
  if (plan.groupBy !== "none") {
    chips.push({
      key: "groupBy",
      kind: "groupBy",
      label: t(`analytics.groupBy.${plan.groupBy}`),
      without: plan.metric === "top" ? null : { ...plan, groupBy: "none" }
    });
  }
  return chips;
}

/** Names of products the answer mentions, for chips of product ids. */
export function productNamesOf(answer: AnalyticsAnswer): Map<string, string> {
  const names = new Map<string, string>();
  for (const purchase of answer.result.evidence) {
    for (const item of purchase.items) if (item.productId && !names.has(item.productId)) names.set(item.productId, item.name);
  }
  for (const group of answer.result.groups) if (group.productId && group.label) names.set(group.productId, group.label);
  return names;
}

export function kpiLabel(t: TFunction, kpi: AnalyticsKpi, priceUnit: string | null): string {
  const unit = priceUnit ? unitShortName(t, priceUnit) : null;
  if (unit && (kpi.code === "averagePrice" || kpi.code === "minimumPrice" || kpi.code === "maximumPrice")) {
    return t(`analytics.kpi.${kpi.code}Per`, { unit });
  }
  return t(`analytics.kpi.${kpi.code}`);
}

export function kpiValue(t: TFunction, locale: string, kpi: AnalyticsKpi, currency: string): string {
  switch (kpi.kind) {
    case "money":
      return kpi.value === null ? "—" : formatMoney(locale, kpi.unit ?? currency, kpi.value);
    case "quantity":
      return kpi.value === null ? "—" : `${formatNumber(locale, kpi.value)} ${kpi.unit ? unitShortName(t, kpi.unit) : ""}`.trim();
    case "date":
      return kpi.date ? formatDay(locale, kpi.date) : "—";
    case "boolean":
      return kpi.value ? t("analytics.kpi.yes") : t("analytics.kpi.no");
    default:
      return kpi.value === null ? "—" : formatNumber(locale, kpi.value);
  }
}

export function groupLabel(t: TFunction, locale: string, answer: AnalyticsAnswer, group: AnalyticsGroup, categories: ProductCategory[]): string {
  if (group.categorySystemKey === "receiptDiscount") return t("analytics.groups.receiptDiscount");
  switch (answer.result.groupBy) {
    case "month": {
      const [year, month] = group.key.split("-").map(Number);
      return new Intl.DateTimeFormat(locale, { month: "short", year: "numeric" }).format(new Date(year, month - 1, 1));
    }
    case "week":
      return t("analytics.groups.week", { day: formatDay(locale, group.key) });
    case "merchant":
      return group.label ?? t("analytics.groups.noMerchant");
    case "category": {
      if (!group.categoryId) return t("analytics.groups.noCategory");
      return categoryPath(t, categories, group.categoryId)
        ?? (group.label ? categoryLabel(t, { name: group.label, systemKey: group.categorySystemKey }) : t("analytics.groups.noCategory"));
    }
    case "brand":
      return group.label ?? t("analytics.groups.noBrand");
    case "tag":
      return group.label ?? t("analytics.groups.noTag");
    default:
      return group.label ?? "—";
  }
}

export function formatGroupValue(t: TFunction, locale: string, answer: AnalyticsAnswer, value: number): string {
  const metric = answer.result.metric;
  if (metric === "quantity") {
    const unit = answer.result.kpis.find((kpi) => kpi.code === "quantity")?.unit;
    return `${formatNumber(locale, value)} ${unit ? unitShortName(t, unit) : ""}`.trim();
  }
  if (metric === "purchaseCount" || metric === "exists") return formatNumber(locale, value);
  return formatMoney(locale, answer.result.currency, value);
}

/** Plain sentences about what the numbers rest on and what they may miss. */
export function coverageLines(t: TFunction, coverage: AnalyticsCoverage, plan: AnalyticsPlan): string[] {
  const lines = [
    t("analytics.coverage.counted", {
      count: coverage.matchedPurchases,
      lines: t("analytics.coverage.lines", { count: coverage.matchedLines }),
      scope: t("analytics.coverage.scope", { count: coverage.purchasesInScope })
    })
  ];
  if (coverage.uncategorizedLinesInScope > 0) {
    lines.push(t(plan.categoryIds.length > 0 ? "analytics.coverage.uncategorizedMissed" : "analytics.coverage.uncategorized", { count: coverage.uncategorizedLinesInScope }));
  }
  if (coverage.unlinkedLinesInScope > 0) lines.push(t("analytics.coverage.unlinked", { count: coverage.unlinkedLinesInScope }));
  if (coverage.unpricedMatchedLines > 0) lines.push(t("analytics.coverage.unpriced", { count: coverage.unpricedMatchedLines }));
  if (coverage.unlinkedDiscountsExcluded > 0) lines.push(t("analytics.coverage.receiptDiscounts", { count: coverage.unlinkedDiscountsExcluded }));
  if (coverage.otherCurrencyLines > 0) lines.push(t("analytics.coverage.otherCurrency", { count: coverage.otherCurrencyLines }));
  if (coverage.otherUnitLines > 0) lines.push(t("analytics.coverage.otherUnit", { count: coverage.otherUnitLines }));
  if (coverage.purchasesWithoutDate > 0) lines.push(t("analytics.coverage.withoutDate", { count: coverage.purchasesWithoutDate }));
  if (coverage.assumedQuantityLines > 0) lines.push(t("analytics.coverage.assumedQuantity", { count: coverage.assumedQuantityLines }));
  if (coverage.truncated) lines.push(t("analytics.coverage.truncated"));
  return lines;
}

export function noteText(t: TFunction, note: AnalyticsNote): string {
  return t(`analytics.notes.${note.code}`, { value: note.value ?? "", defaultValue: t("analytics.notes.generic") });
}
