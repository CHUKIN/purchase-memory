export type ReceiptStatus = "uploaded" | "queued" | "processing" | "ready" | "failed" | "saved";

/**
 * Where a recognized value sits on the receipt photo, in fractions of the image
 * size (0.0-1.0). Best effort: recognition often cannot place a value at all,
 * and the backend drops any box that fails validation.
 */
export interface BoundingBox {
  x: number;
  y: number;
  width: number;
  height: number;
}

/**
 * A discount ("OPUST", "RABAT") is its own line right under its product, with a
 * negative `lineTotal`, so all lines still add up to the receipt total.
 */
export type ReceiptLineKind = "product" | "discount";

export interface ReceiptItemSnapshot {
  name: string;
  quantity: number | null;
  unit: string | null;
  unitPrice: number | null;
  lineTotal: number | null;
  boundingBox?: BoundingBox | null;
  /** Omitted by older payloads; the backend then treats a negative amount as a discount. */
  kind?: ReceiptLineKind;
  /**
   * For a discount: zero-based position of the product it reduces. Output only:
   * the backend recomputes it from line order on every save.
   */
  discountedItemPosition?: number | null;
}

export interface ReceiptSnapshot {
  merchantName: string | null;
  purchasedAt: string | null;
  currency: string | null;
  receiptTotal: number | null;
  items: ReceiptItemSnapshot[];
  receiptTotalBoundingBox?: BoundingBox | null;
}

export type PurchaseDraftIssueType = "receiptTotalMismatch" | "missingItemPrice";

export interface PurchaseDraftIssue {
  type: PurchaseDraftIssueType;
  /** Zero-based item position, or `null` for a receipt-level issue. */
  itemPosition: number | null;
  itemName: string | null;
  /** What recognition produced, already formatted; empty when nothing was recognized. */
  recognizedValue: string;
  /** English fallback copy; the UI renders localized text from `type` instead. */
  suggestedFix: string;
  /** Where the value is on the photo, when recognition returned a usable box. */
  boundingBox?: BoundingBox | null;
}

export interface ReceiptView {
  id: string;
  status: ReceiptStatus;
  uploadedAt: string;
  updatedAt: string;
  originalFileName: string;
  sourceImageUrl: string;
  imageParts: ReceiptImagePartView[];
  provider: string | null;
  model: string | null;
  processingDurationMs: number | null;
  snapshot: ReceiptSnapshot | null;
  /** Flat recognition warnings, including ones this slice does not structure. */
  warnings: string[];
  /** The actionable subset of `warnings`, structured per field or item. */
  issues: PurchaseDraftIssue[];
  error: string | null;
  savedPurchaseId: string | null;
}

export interface ReceiptImagePartView {
  position: number;
  contentType: string;
  sizeBytes: number;
  sourceImageUrl: string;
}

export type ReceiptReviewAuditChangeType = "Modified" | "ItemAdded" | "ItemRemoved";

/** One append-only record of a human correction made during receipt review. */
export interface ReceiptReviewAuditEntry {
  occurredAt: string;
  actor: string;
  fieldName: string;
  changeType: ReceiptReviewAuditChangeType;
  oldValue: string | null;
  newValue: string | null;
}

export interface ReceiptUploadResult {
  receipt: ReceiptView;
  exactDuplicate: boolean;
}

export interface PurchaseItemView {
  name: string;
  quantity: number | null;
  unit: string | null;
  unitPrice: number | null;
  lineTotal: number | null;
  kind?: ReceiptLineKind;
  discountedItemPosition?: number | null;
  /**
   * Only set while the feed is filtered by category or brand: `true` for the
   * lines whose product made the purchase match.
   */
  matchesFilter?: boolean | null;
}

export interface PurchaseView {
  id: string;
  merchantName: string | null;
  purchasedAt: string | null;
  currency: string | null;
  receiptTotal: number | null;
  createdAt: string;
  updatedAt: string;
  items: PurchaseItemView[];
  issues: PurchaseDraftIssue[];
}

/** How the backend should link a line to the catalog when a purchase is corrected. */
export type ProductLinkChoice = "auto" | "existing" | "new";

export interface PurchaseProductCandidate {
  productId: string;
  name: string;
  score: number;
}

export interface PurchaseDetailItem {
  /** Stable only until the purchase is corrected; impressions follow the line across a correction. */
  id: string;
  position: number;
  name: string;
  quantity: number | null;
  unit: string | null;
  unitPrice: number | null;
  lineTotal: number | null;
  kind: ReceiptLineKind;
  discountedItemPosition: number | null;
  productId: string | null;
  productName: string | null;
  /** The matcher could not decide on its own; `candidates` lists what it considered. */
  pendingMatch: boolean;
  candidates: PurchaseProductCandidate[];
  boundingBox: BoundingBox | null;
}

/** One saved purchase with its catalog links, as the detail page and editor see it. */
export interface PurchaseDetail {
  id: string;
  sourceReceiptId: string | null;
  merchantName: string | null;
  purchasedAt: string | null;
  currency: string | null;
  receiptTotal: number | null;
  createdAt: string;
  /** Sent back as `expectedUpdatedAt`; a newer stored value makes the save a 409. */
  updatedAt: string;
  items: PurchaseDetailItem[];
  issues: PurchaseDraftIssue[];
}

export interface UpdatePurchaseItem {
  name: string;
  quantity: number | null;
  unit: string | null;
  unitPrice: number | null;
  lineTotal: number | null;
  kind: ReceiptLineKind;
  productLink: ProductLinkChoice;
  productId: string | null;
  boundingBox: BoundingBox | null;
}

/** A complete corrected snapshot: lines left out are removed. */
export interface UpdatePurchaseRequest {
  expectedUpdatedAt: string;
  merchantName: string | null;
  purchasedAt: string | null;
  currency: string | null;
  receiptTotal: number | null;
  items: UpdatePurchaseItem[];
}

export type PurchaseReviewFilter = "all" | "needsAttention" | "reviewed";

export interface PurchaseFeedPage {
  items: PurchaseView[];
  page: number;
  pageSize: number;
  totalCount: number;
  hasMore: boolean;
  /** The server reached its bounded candidate window; older purchases were not scanned. */
  truncated: boolean;
}

export interface RecognitionStatus {
  available: boolean;
  mode: string;
  models: string[];
  message: string | null;
}

/** Who decided a product's category or brand; `null` means nobody has yet. */
export type AttributeSource = "suggested" | "confirmed";

/** One node of the two-level category tree, parents first, each followed by its children. */
/** One brand for the filter list; `brand` is the most common spelling. */
export interface BrandSummary {
  brand: string;
  productCount: number;
}

export interface ProductCategory {
  id: string;
  /** `null` for a top-level group. */
  parentId: string | null;
  name: string;
  /** Stable key of a seeded category, translated by the UI; `null` once renamed or for the household's own. */
  systemKey: string | null;
  position: number;
  /** Products directly in this category (not counting subcategories). */
  productCount: number;
}

export interface ProductClassification {
  productId: string;
  categoryId: string | null;
  categorySource: AttributeSource | null;
  brand: string | null;
  brandSource: AttributeSource | null;
}

export interface CategorizationRunSummary {
  configured: boolean;
  considered: number;
  suggested: number;
  failedBatches: number;
  inputTokens: number;
  outputTokens: number;
}

/** A household tag as it appears on a product. */
export interface ProductTag {
  id: string;
  name: string;
}

/** A household tag in the list, with how many products carry it. */
export interface TagSummary extends ProductTag {
  productCount: number;
}

export interface ProductSummary {
  id: string;
  name: string;
  normalizedName: string;
  /** `null` is «Без категории». Optional only so older fixtures stay valid. */
  categoryId?: string | null;
  categorySource?: AttributeSource | null;
  brand?: string | null;
  /** Alphabetical. Optional only so older fixtures stay valid. */
  tags?: ProductTag[];
  /** Distinct shopping trips, the same definition the product page counts by. */
  purchaseCount: number;
  /** Receipt lines behind those trips; two jars on one receipt are two lines. */
  linkedItemCount: number;
  aliasCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface ProductCatalogPage {
  items: ProductSummary[];
  page: number;
  pageSize: number;
  totalCount: number;
  hasMore: boolean;
  /** Items the matcher left for a person to decide on; the queue itself is UI-4 stage 3. */
  pendingMatchCount: number;
}

/** One receipt line's price, already reduced to «what one unit cost». */
export interface ProductPricePoint {
  purchasedAt: string;
  merchantName: string | null;
  unitPrice: number;
  purchaseId: string;
}

/**
 * Prices that may legitimately be compared with each other. The backend never
 * mixes units or currencies, so a product sold both per kilogram and per piece
 * arrives as two series rather than one misleading average.
 */
export interface ProductPriceSeries {
  currency: string | null;
  /** Canonical unit token, for example `szt` or `kg`. */
  unit: string;
  minimumPrice: number;
  averagePrice: number;
  latestPrice: number;
  latestPoint: ProductPricePoint;
  points: ProductPricePoint[];
}

export interface ProductFrequency {
  firstPurchasedAt: string;
  lastPurchasedAt: string;
  purchaseCount: number;
  /** Null when there is only one purchase, or when they all fall on one day. */
  averageDaysBetweenPurchases: number | null;
}

export interface ProductAlias {
  merchantKey: string;
  rawText: string;
}

export interface ProductDetail {
  id: string;
  name: string;
  normalizedName: string;
  categoryId?: string | null;
  categorySource?: AttributeSource | null;
  brand?: string | null;
  brandSource?: AttributeSource | null;
  /** Alphabetical. Optional only so older fixtures stay valid. */
  tags?: ProductTag[];
  createdAt: string;
  updatedAt: string;
  /** Distinct shopping trips, matching what the frequency block states. */
  purchaseCount: number;
  linkedItemCount: number;
  /** Linked lines whose price could not be read; the history below is that much incomplete. */
  unpricedItemCount: number;
  /** Best-evidenced series first. */
  priceSeries: ProductPriceSeries[];
  frequency: ProductFrequency | null;
  aliases: ProductAlias[];
}

export type AnalyticsMetric =
  | "spend"
  | "quantity"
  | "purchaseCount"
  | "averageUnitPrice"
  | "minimumUnitPrice"
  | "maximumUnitPrice"
  | "exists"
  | "top";

/** `tag` counts a line under every tag its product has, so tag rows may add up to more than the total. */
export type AnalyticsGroupBy = "none" | "month" | "week" | "merchant" | "category" | "product" | "brand" | "tag";

export type AnalyticsDiscountFilter = "any" | "discountedOnly" | "notDiscounted";

/** The typed query the server runs. Product selectors (ids, categories, name terms) are alternatives; tags, brands and the rest narrow them. */
export interface AnalyticsPlan {
  metric: AnalyticsMetric;
  productIds: string[];
  categoryIds: string[];
  nameTerms: string[];
  brands: string[];
  /** A line needs every one of these tags, like the purchases filter. */
  tagIds: string[];
  merchants: string[];
  /** Inclusive local day, `YYYY-MM-DD`. */
  from: string | null;
  to: string | null;
  discount: AnalyticsDiscountFilter;
  groupBy: AnalyticsGroupBy;
  limit: number | null;
}

export interface AnalyticsNote {
  code: string;
  value: string | null;
}

export interface AnalyticsKpi {
  code: string;
  kind: "money" | "count" | "quantity" | "date" | "boolean";
  value: number | null;
  unit: string | null;
  date: string | null;
}

export interface AnalyticsGroup {
  key: string;
  label: string | null;
  value: number;
  lineCount: number;
  categoryId: string | null;
  categorySystemKey: string | null;
  productId: string | null;
  /** Set on tag rows; the «no tag» row has none. */
  tagId?: string | null;
}

export interface AnalyticsEvidenceItem {
  name: string;
  productId: string | null;
  lineTotal: number | null;
  discountTotal: number;
}

export interface AnalyticsEvidencePurchase {
  purchaseId: string;
  sourceReceiptId: string | null;
  purchasedAt: string;
  merchantName: string | null;
  currency: string;
  amount: number;
  matchedLineCount: number;
  items: AnalyticsEvidenceItem[];
}

export interface AnalyticsCoverage {
  purchasesInScope: number;
  matchedPurchases: number;
  matchedLines: number;
  uncategorizedLinesInScope: number;
  unlinkedLinesInScope: number;
  unpricedMatchedLines: number;
  unlinkedDiscountsExcluded: number;
  otherCurrencyLines: number;
  otherUnitLines: number;
  purchasesWithoutDate: number;
  truncated: boolean;
  /** Quantity answers: matching lines without a printed quantity, counted as one piece. */
  assumedQuantityLines: number;
  /** Matching lines whose product has several tags: a breakdown by tag counts them more than once. */
  multiTaggedLines?: number;
}

export interface AnalyticsResult {
  metric: AnalyticsMetric;
  groupBy: AnalyticsGroupBy;
  currency: string;
  hasData: boolean;
  kpis: AnalyticsKpi[];
  groups: AnalyticsGroup[];
  hiddenGroupCount: number;
  evidence: AnalyticsEvidencePurchase[];
  evidenceTotal: number;
  coverage: AnalyticsCoverage;
}

export interface AnalyticsAnswer {
  plan: AnalyticsPlan;
  notes: AnalyticsNote[];
  conclusion: string;
  result: AnalyticsResult;
  today: string;
}

/** An asked question's answer, plus where it was saved in the household's history. */
export interface AnalyticsAskResponse extends AnalyticsAnswer {
  /** Null only if saving to the history failed; the answer still stands. */
  historyId: string | null;
  /** The same question was asked before: its saved plan ran without the model. */
  reused: boolean;
}

/** One line of «Предыдущие вопросы». */
export interface AnalyticsHistoryItem {
  id: string;
  question: string;
  /** Asked as a refinement of an earlier answer. */
  isFollowUp: boolean;
  conclusion: string;
  askedByEmail: string | null;
  askedByMe: boolean;
  createdAt: string;
  /** When the saved answer was computed; the list is ordered by it, newest first. */
  answeredAt: string;
}

export interface AnalyticsHistoryPage {
  items: AnalyticsHistoryItem[];
  hasMore: boolean;
}

export interface AnalyticsHistoryDetail {
  entry: AnalyticsHistoryItem;
  /** Null only for an entry saved by an incompatible older version; «Обновить» recalculates it. */
  answer: AnalyticsAnswer | null;
}

export interface AnalyticsContext {
  enabled: boolean;
  today: string;
  merchants: string[];
  brands: string[];
}

export type ReviewQueueKind =
  | "failedReceipt"
  | "unsavedReceipt"
  | "totalMismatch"
  | "missingPrice"
  | "pendingMatch"
  | "suggestedCategory"
  | "missingCategory";

/** One thing the stored data still needs a person for; only the fields of its kind are set. */
export interface ReviewQueueItem {
  kind: ReviewQueueKind;
  /** Stable across calls: kind plus entity id. */
  id: string;
  /** Merchant for receipts and purchases, product or line name otherwise; may be empty. */
  title: string;
  occurredAt: string;
  receiptId: string | null;
  purchaseId: string | null;
  productId: string | null;
  categoryId: string | null;
  categoryName: string | null;
  currency: string | null;
  receiptTotal: number | null;
  linesTotal: number | null;
  affectedLineCount: number | null;
  /** Zero-based line to focus in the purchase editor (pending match, first missing price). */
  itemPosition?: number | null;
  /** Pending match only (empty otherwise): suggested products, best first, at most three. */
  candidates?: ReviewQueueCandidate[];
}

/** A catalog product the matcher considered for an unmatched receipt line. */
export interface ReviewQueueCandidate {
  productId: string;
  name: string;
  brand: string | null;
  categoryId: string | null;
}

export interface ReviewQueueGroup {
  kind: ReviewQueueKind;
  totalCount: number;
  items: ReviewQueueItem[];
}

export interface ReviewQueue {
  totalCount: number;
  groups: ReviewQueueGroup[];
}

export interface ReviewQueueCount {
  totalCount: number;
  byKind: Partial<Record<ReviewQueueKind, number>>;
}

/** The confirmed three-level scale: не нравится / средне / нравится. */
export type ImpressionRating = "dislike" | "neutral" | "like";

/** One person's opinion of one purchased line, with the store, date and price it was judged in. */
export interface Impression {
  id: string;
  purchaseId: string;
  /** Null when a purchase correction removed the line; the impression stays with the purchase. */
  purchaseItemId: string | null;
  productId: string | null;
  itemName: string;
  rating: ImpressionRating;
  note: string | null;
  /** Protected API URL of the impression photo (not a product photo). */
  photoUrl: string | null;
  authorUserId: string;
  authorEmail: string | null;
  isMine: boolean;
  createdAt: string;
  merchantName: string | null;
  purchasedAt: string | null;
  currency: string | null;
  quantity: number | null;
  unit: string | null;
  unitPrice: number | null;
  lineTotal: number | null;
}

/** A bought line that can be rated. */
export interface RateablePurchaseItem {
  purchaseItemId: string;
  purchaseId: string;
  productId: string | null;
  productName: string | null;
  itemName: string;
  merchantName: string | null;
  purchasedAt: string;
  currency: string | null;
  unitPrice: number | null;
  lineTotal: number | null;
}
