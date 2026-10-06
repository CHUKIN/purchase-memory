import type { AnalyticsAnswer, AnalyticsCoverage, AnalyticsPlan } from "../../api/types";

export const basePlan: AnalyticsPlan = {
  metric: "spend",
  productIds: [],
  categoryIds: [],
  nameTerms: [],
  brands: [],
  tagIds: [],
  merchants: [],
  from: null,
  to: null,
  discount: "any",
  groupBy: "none",
  limit: null
};

const coverage = (overrides: Partial<AnalyticsCoverage> = {}): AnalyticsCoverage => ({
  purchasesInScope: 42,
  matchedPurchases: 14,
  matchedLines: 17,
  uncategorizedLinesInScope: 23,
  unlinkedLinesInScope: 2,
  unpricedMatchedLines: 0,
  unlinkedDiscountsExcluded: 0,
  otherCurrencyLines: 0,
  otherUnitLines: 0,
  purchasesWithoutDate: 0,
  truncated: false,
  assumedQuantityLines: 0,
  ...overrides
});

const months = ["2026-01", "2026-02", "2026-03", "2026-04", "2026-05", "2026-06", "2026-07", "2026-08", "2026-09"];

/** «Сколько мы купили молока за год?» — quantity by month, with a category and a receipt word. */
export const milkQuantityAnswer: AnalyticsAnswer = {
  plan: { ...basePlan, metric: "quantity", categoryIds: ["milk"], nameTerms: ["mleko"], from: "2026-01-01", to: "2026-09-26", groupBy: "month" },
  notes: [],
  conclusion: "Куплено 21 шт.: 17 позиций в 14 чеках.",
  today: "2026-09-26",
  result: {
    metric: "quantity",
    groupBy: "month",
    currency: "PLN",
    hasData: true,
    kpis: [
      { code: "quantity", kind: "quantity", value: 21, unit: "szt", date: null },
      { code: "lineCount", kind: "count", value: 17, unit: null, date: null },
      { code: "purchaseCount", kind: "count", value: 14, unit: null, date: null }
    ],
    groups: months.map((key, index) => ({
      key, label: null, value: [2, 3, 1, 2, 4, 2, 3, 2, 2][index], lineCount: 2, categoryId: null, categorySystemKey: null, productId: null
    })),
    hiddenGroupCount: 0,
    evidence: [
      {
        purchaseId: "p1", sourceReceiptId: "r1", purchasedAt: "2026-09-20T15:10:00Z", merchantName: "Biedronka", currency: "PLN", amount: 8.58, matchedLineCount: 2,
        items: [
          { name: "MLEKO LACIATE 3,2% 1L", productId: "prod-milk", lineTotal: 4.79, discountTotal: -1.0 },
          { name: "MLEKO UHT 2%", productId: "prod-uht", lineTotal: 4.79, discountTotal: 0 }
        ]
      },
      {
        purchaseId: "p2", sourceReceiptId: null, purchasedAt: "2026-09-02T09:00:00Z", merchantName: "Lidl", currency: "PLN", amount: 4.29, matchedLineCount: 1,
        items: [{ name: "Mleko Pilos 1L", productId: null, lineTotal: 4.29, discountTotal: 0 }]
      }
    ],
    evidenceTotal: 14,
    coverage: coverage({ unlinkedDiscountsExcluded: 1, assumedQuantityLines: 3 })
  }
};

/** «Сколько потратили в Biedronce в августе?» — whole-receipt spend, no breakdown. */
export const biedronkaSpendAnswer: AnalyticsAnswer = {
  plan: { ...basePlan, merchants: ["Biedronka"], from: "2026-08-01", to: "2026-08-31" },
  notes: [],
  conclusion: "Потрачено 612,40 zł: 88 позиций в 9 чеках. Скидки сэкономили 31,15 zł.",
  today: "2026-09-26",
  result: {
    metric: "spend",
    groupBy: "none",
    currency: "PLN",
    hasData: true,
    kpis: [
      { code: "spend", kind: "money", value: 612.4, unit: "PLN", date: null },
      { code: "discounts", kind: "money", value: -31.15, unit: "PLN", date: null },
      { code: "purchaseCount", kind: "count", value: 9, unit: null, date: null },
      { code: "lineCount", kind: "count", value: 88, unit: null, date: null }
    ],
    groups: [],
    hiddenGroupCount: 0,
    evidence: milkQuantityAnswer.result.evidence,
    evidenceTotal: 9,
    coverage: coverage({ purchasesInScope: 9, matchedPurchases: 9, matchedLines: 88, uncategorizedLinesInScope: 12, unlinkedLinesInScope: 0 })
  }
};

/** «Сколько в среднем стоил кофе этой марки?» — average price with min and max, and a dropped store. */
export const coffeePriceAnswer: AnalyticsAnswer = {
  plan: { ...basePlan, metric: "averageUnitPrice", categoryIds: ["coffee"], brands: ["Lavazza"], groupBy: "merchant" },
  notes: [{ code: "unknownMerchant", value: "Carrefour" }],
  conclusion: "Средняя цена — 54,90 zł за шт. по 6 покупкам, от 44,99 zł до 62,99 zł.",
  today: "2026-09-26",
  result: {
    metric: "averageUnitPrice",
    groupBy: "merchant",
    currency: "PLN",
    hasData: true,
    kpis: [
      { code: "averagePrice", kind: "money", value: 54.9, unit: "PLN", date: null },
      { code: "minimumPrice", kind: "money", value: 44.99, unit: "PLN", date: null },
      { code: "maximumPrice", kind: "money", value: 62.99, unit: "PLN", date: null },
      { code: "priceCount", kind: "count", value: 6, unit: "szt", date: null }
    ],
    groups: [
      { key: "lidl", label: "Lidl", value: 58.49, lineCount: 2, categoryId: null, categorySystemKey: null, productId: null },
      { key: "biedronka", label: "Biedronka", value: 53.1, lineCount: 4, categoryId: null, categorySystemKey: null, productId: null }
    ],
    hiddenGroupCount: 0,
    evidence: [{
      purchaseId: "p3", sourceReceiptId: "r3", purchasedAt: "2026-09-12T10:00:00Z", merchantName: "Biedronka", currency: "PLN", amount: 44.99, matchedLineCount: 1,
      items: [{ name: "KAWA LAVAZZA ORO 1KG", productId: "prod-coffee", lineTotal: 49.99, discountTotal: -5 }]
    }],
    evidenceTotal: 6,
    coverage: coverage({ matchedPurchases: 6, matchedLines: 6, otherUnitLines: 1 })
  }
};

/** «Как много мы купили сладостей?» — top products in a category group. */
export const sweetsTopAnswer: AnalyticsAnswer = {
  plan: { ...basePlan, metric: "top", nameTerms: ["czekolad", "baton"], groupBy: "product", limit: 5, from: "2026-01-01", to: "2026-09-26" },
  notes: [],
  conclusion: "Больше всего потрачено на «CZEKOLADA MILKA 100G»: 86,40 zł из 214,70 zł.",
  today: "2026-09-26",
  result: {
    metric: "top",
    groupBy: "product",
    currency: "PLN",
    hasData: true,
    kpis: [
      { code: "spend", kind: "money", value: 214.7, unit: "PLN", date: null },
      { code: "purchaseCount", kind: "count", value: 19, unit: null, date: null },
      { code: "lineCount", kind: "count", value: 31, unit: null, date: null }
    ],
    groups: [
      ["CZEKOLADA MILKA 100G", 86.4], ["PRINCESSA KOKOS", 41.2], ["ŻELKI HARIBO ZŁOTE MISIE 175G — WYJĄTKOWO DŁUGA NAZWA", 33.5], ["WAFLE PRINCE POLO", 29.8], ["DELICJE SZAMPAŃSKIE", 23.8]
    ].map(([label, value], index) => ({
      key: `prod-${index}`, label: label as string, value: value as number, lineCount: 3, categoryId: null, categorySystemKey: null, productId: `prod-${index}`
    })),
    hiddenGroupCount: 7,
    evidence: [],
    evidenceTotal: 0,
    coverage: coverage({ matchedPurchases: 19, matchedLines: 31 })
  }
};

/** «Покупал ли я макароны из цельнозерновой муки?» — no. */
export const pastaNoAnswer: AnalyticsAnswer = {
  plan: { ...basePlan, metric: "exists", nameTerms: ["makaron pelnoziarn"] },
  notes: [],
  conclusion: "Нет, таких покупок не нашлось.",
  today: "2026-09-26",
  result: {
    metric: "exists",
    groupBy: "none",
    currency: "PLN",
    hasData: false,
    kpis: [
      { code: "exists", kind: "boolean", value: 0, unit: null, date: null },
      { code: "purchaseCount", kind: "count", value: 0, unit: null, date: null }
    ],
    groups: [],
    hiddenGroupCount: 0,
    evidence: [],
    evidenceTotal: 0,
    coverage: coverage({ purchasesInScope: 120, matchedPurchases: 0, matchedLines: 0, uncategorizedLinesInScope: 64 })
  }
};

export const sampleAnalyticsTags = [
  { id: "tag-kid", name: "Для ребёнка" },
  { id: "tag-promo", name: "Акция" },
  { id: "tag-holiday", name: "К празднику" }
];

/** «Сколько потратили на детское и что из этого по акции?» — a tag filter with a breakdown by tag. */
export const kidsByTagAnswer: AnalyticsAnswer = {
  plan: { ...basePlan, tagIds: ["tag-kid"], from: "2026-09-01", to: "2026-09-26", groupBy: "tag" },
  notes: [{ code: "unknownTag", value: "9d3c0b8e-0000-0000-0000-000000000000" }],
  conclusion: "Потрачено 186,30 zł: 12 позиций в 7 чеках. Учтены только товары с тегом «Для ребёнка».",
  today: "2026-09-26",
  result: {
    metric: "spend",
    groupBy: "tag",
    currency: "PLN",
    hasData: true,
    kpis: [
      { code: "spend", kind: "money", value: 186.3, unit: "PLN", date: null },
      { code: "purchaseCount", kind: "count", value: 7, unit: null, date: null },
      { code: "lineCount", kind: "count", value: 12, unit: null, date: null }
    ],
    groups: [
      { key: "tag-kid", label: "Для ребёнка", value: 186.3, lineCount: 12, categoryId: null, categorySystemKey: null, productId: null, tagId: "tag-kid" },
      { key: "tag-promo", label: "Акция", value: 54.2, lineCount: 4, categoryId: null, categorySystemKey: null, productId: null, tagId: "tag-promo" },
      { key: "tag-holiday", label: "К празднику", value: 21.9, lineCount: 1, categoryId: null, categorySystemKey: null, productId: null, tagId: "tag-holiday" }
    ],
    hiddenGroupCount: 0,
    evidence: milkQuantityAnswer.result.evidence,
    evidenceTotal: 7,
    coverage: coverage({ purchasesInScope: 18, matchedPurchases: 7, matchedLines: 12, uncategorizedLinesInScope: 0, multiTaggedLines: 5 })
  }
};

/** All purchases by tag: untagged lines form their own row. */
export const allByTagAnswer: AnalyticsAnswer = {
  ...kidsByTagAnswer,
  plan: { ...kidsByTagAnswer.plan, tagIds: [] },
  notes: [],
  conclusion: "Потрачено 1 240,00 zł: 96 позиций в 18 чеках.",
  result: {
    ...kidsByTagAnswer.result,
    groups: [
      { key: "", label: null, value: 1012.4, lineCount: 80, categoryId: null, categorySystemKey: null, productId: null, tagId: null },
      ...kidsByTagAnswer.result.groups
    ]
  }
};
