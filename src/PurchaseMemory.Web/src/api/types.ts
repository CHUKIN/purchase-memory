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
 * One outline around printed words of a line, as flat `[x0, y0, x1, y1, …]`
 * pairs in the same photo fractions as `BoundingBox`. A line's highlight is the
 * union of its outlines, so it follows a tilted or two-row printed line.
 */
export type TextPolygon = number[];

/**
 * Where a line's highlight comes from: the rectangle recognition returned
 * (`recognition`), or the printed words Azure found for the line (`azure`).
 */
export type BoxSource = "recognition" | "azure";

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
  /**
   * Output only: the line's printed words on the photo, when the server found
   * them; preferred over `boundingBox`. The server re-matches them after every
   * save, so they are never sent back.
   */
  textPolygons?: TextPolygon[] | null;
  boxSource?: BoxSource;
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
  /** The item's printed words on the photo, when found; preferred over `boundingBox`. */
  textPolygons?: TextPolygon[] | null;
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
  /** Every product line has one; only discount lines (and very old data) are null. */
  productId: string | null;
  productName: string | null;
  /** The linked product's human Polish name, shown under the receipt text as «→ …». */
  productNamePl?: string | null;
  /** The linked product carries the «проверить» flag. Optional only so older fixtures stay valid. */
  productNeedsReview?: boolean;
  boundingBox: BoundingBox | null;
  /** The line's printed words on the photo (see `ReceiptItemSnapshot.textPolygons`). */
  textPolygons?: TextPolygon[] | null;
  boxSource?: BoxSource;
  /**
   * Someone confirmed in the swipe review that the line matches the receipt.
   * Always sent by the server; optional so older fixtures stay valid.
   */
  approved?: boolean;
  approvedAt?: string | null;
  approvedByEmail?: string | null;
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
  /** The saved line this one continues; keeps its «checked» mark. Null for a new line. */
  id?: string | null;
}

/** One «checked» mark from the swipe review, as the phone queues it. */
export interface LineApprovalChange {
  purchaseId: string;
  lineId: string;
  approved: boolean;
  /** When the person swiped (ISO), by the phone's clock. */
  at: string;
}

export interface LineApprovalResult {
  purchaseId: string;
  lineId: string;
  /** `notFound`: no such purchase or line (e.g. replaced by a correction); nothing changed. */
  status: "applied" | "unchanged" | "notFound";
  approved: boolean;
  approvedAt: string | null;
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
  /** Products that got a new suggested human name (also counted in `suggested`). */
  namesSuggested?: number;
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

/** An AI hint: an existing household tag that may fit the product. Never applied by itself. */
export interface TagSuggestion {
  tagId: string;
  name: string;
  /** A short phrase in the requested language. */
  reason: string;
}

export interface TagSuggestionsResponse {
  productId: string;
  suggestions: TagSuggestion[];
  /** Household tags the product does not have yet; 0 means there was nothing to suggest from. */
  offeredTagCount: number;
}

/** Where a product first came from. */
export type ProductOrigin = "receipt" | "manual" | "barcode";

export interface ProductSummary {
  id: string;
  name: string;
  normalizedName: string;
  /** The «проверить» flag. Optional only so older fixtures stay valid. */
  needsReview?: boolean;
  origin?: ProductOrigin;
  /** `null` is «Без категории». Optional only so older fixtures stay valid. */
  categoryId?: string | null;
  categorySource?: AttributeSource | null;
  brand?: string | null;
  /** Human Polish name (stage 1.4); `null`/absent falls back to `name`, the receipt text. */
  namePl?: string | null;
  namePlSource?: AttributeSource | null;
  /** Russian translation of the Polish name. */
  nameRu?: string | null;
  nameRuSource?: AttributeSource | null;
  /** The main product photo (thumbnail at `/api/product-photos/{id}/thumbnail`); null/absent without photos. */
  mainPhotoId?: string | null;
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
  /** Flagged («проверить») products in the whole catalog, whatever the filter. */
  needsReviewCount: number;
}

/** An existing product a flagged product might really be. */
export interface ProductReviewCandidate {
  productId: string;
  name: string;
  brand: string | null;
  categoryId: string | null;
  purchaseCount: number;
  score: number;
  namePl?: string | null;
  nameRu?: string | null;
}

export interface CreateProductRequest {
  name: string;
  brand?: string | null;
  categoryId?: string | null;
  origin: "manual" | "barcode";
}

/** What `POST /api/products`, rename and «Это другой товар» answer with. */
export interface ProductChangeResult {
  id: string;
  name: string;
  normalizedName: string;
  origin: ProductOrigin;
  needsReview: boolean;
  categoryId: string | null;
  categorySource: AttributeSource | null;
  brand: string | null;
  brandSource: AttributeSource | null;
  /** Human Polish name (stage 1.4); `null`/absent falls back to `name`, the receipt text. */
  namePl?: string | null;
  namePlSource?: AttributeSource | null;
  /** Russian translation of the Polish name. */
  nameRu?: string | null;
  nameRuSource?: AttributeSource | null;
  createdAt: string;
  updatedAt: string;
}

/** A product's human names after an edit, «Верно» or «Предложить заново». */
export interface ProductNames {
  productId: string;
  namePl: string | null;
  namePlSource: AttributeSource | null;
  nameRu: string | null;
  nameRuSource: AttributeSource | null;
}

export type ProductNameLanguage = "pl" | "ru";

/**
 * One language after «Название по фото»: `replaced` (the previous name is what
 * «Вернуть» puts back), `proposed` (a person's name stays; `name` is offered),
 * `unchanged` or `notRead` (the model was not sure).
 */
export interface PhotoNameLanguageResult {
  language: ProductNameLanguage;
  outcome: "replaced" | "proposed" | "unchanged" | "notRead";
  name: string | null;
  previousName: string | null;
  previousSource: AttributeSource | null;
}

export interface ProductPhotoNameResult extends ProductNames {
  languages: PhotoNameLanguageResult[];
}

/** «Найти по фото»: whether the server offers it (switched on and configured). */
export interface PhotoSearchStatus {
  enabled: boolean;
}

export type PhotoMatchConfidence = "high" | "medium" | "low";

/** One of the household's products the photo most likely shows. */
export interface PhotoSearchMatch {
  productId: string;
  /** The receipt text (the catalog's canonical name). */
  name: string;
  namePl: string | null;
  nameRu: string | null;
  brand: string | null;
  category: string | null;
  mainPhotoId: string | null;
  purchaseCount: number;
  lastPurchasedAt: string | null;
  confidence: PhotoMatchConfidence;
}

export interface PhotoSearchResult {
  /** Best first, at most three; empty when nothing on the list fits. */
  matches: PhotoSearchMatch[];
  /** How many of the household's products the model chose from. */
  candidateCount: number;
}

export interface ProductMergeResult {
  sourceProductId: string;
  sourceName: string;
  targetProductId: string;
  targetName: string;
  movedLineCount: number;
  movedAliasCount: number;
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
  /** Human Polish name (stage 1.4); `null`/absent falls back to `name`, the receipt text. */
  namePl?: string | null;
  namePlSource?: AttributeSource | null;
  /** Russian translation of the Polish name. */
  nameRu?: string | null;
  nameRuSource?: AttributeSource | null;
  /** The main product photo; null/absent without photos. */
  mainPhotoId?: string | null;
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
  origin?: ProductOrigin;
  /** The «проверить» flag. Optional only so older fixtures stay valid. */
  needsReview?: boolean;
  reviewReason?: "ambiguousMatch" | null;
  /** Flagged products only: what it might really be, best first. */
  reviewCandidates?: ProductReviewCandidate[];
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
  | "productNeedsReview"
  | "suggestedCategory"
  | "missingCategory"
  /** A saved purchase with lines nobody has marked «Верно» yet (hub only). */
  | "unapprovedLines"
  /** A recent line the signed-in person has not rated yet (hub only, personal). */
  | "unratedPurchase"
  /** AI-suggested human names nobody has confirmed yet (hub only). */
  | "suggestedNames";

/** The «Ревью» hub's sections from the server, most important first. */
export type ReviewQueueSection = "receipts" | "purchases" | "products" | "names" | "categories" | "ratings";

export interface ReviewQueueSectionCount {
  section: ReviewQueueSection;
  count: number;
}

/** The whole hub, never filtered: `totalCount` is the sum of `sections`. */
export interface ReviewQueueSummary {
  totalCount: number;
  sections: ReviewQueueSectionCount[];
}

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
  /** Zero-based line to focus in the purchase editor (first missing price). */
  itemPosition?: number | null;
  /** Flagged product only (empty otherwise): what it might really be, best first, at most three. */
  candidates?: ReviewQueueCandidate[];
  /** Flagged product only: distinct purchases it appears on. */
  purchaseCount?: number | null;
  /**
   * Flagged product only: it has no category either. The same card then asks
   * for one, and the product is not listed again under «Без категории».
   */
  needsCategory?: boolean;
  /** Unchecked purchase only: all its lines (`affectedLineCount` is the unchecked ones). */
  lineCount?: number | null;
  /** Unrated line only: the line to rate, its store and price. */
  purchaseItemId?: string | null;
  merchantName?: string | null;
  price?: number | null;
  /** Product kinds: the human names (`null`: show `title`, the receipt text). */
  namePl?: string | null;
  nameRu?: string | null;
  /** Suggested names only: which of the two still wait for «Верно». */
  namePlSuggested?: boolean;
  nameRuSuggested?: boolean;
  /*
   * `categoryId`/`categoryName` on a flagged product: its category is only
   * AI-suggested; the same card asks to confirm it (no «Категория не
   * подтверждена» item for it).
   */
}

/** An existing product a flagged product might really be. */
export interface ReviewQueueCandidate {
  productId: string;
  name: string;
  brand: string | null;
  categoryId: string | null;
  /** Optional only so older fixtures stay valid. */
  purchaseCount?: number;
  score?: number;
  namePl?: string | null;
  nameRu?: string | null;
}

export interface ReviewQueueGroup {
  kind: ReviewQueueKind;
  totalCount: number;
  items: ReviewQueueItem[];
}

export interface ReviewQueue {
  /** Follows the filter, like `groups`. */
  totalCount: number;
  groups: ReviewQueueGroup[];
  /** The whole hub whatever the filter. Optional only so older fixtures stay valid. */
  summary?: ReviewQueueSummary | null;
}

export interface ReviewQueueCount {
  totalCount: number;
  byKind: Partial<Record<ReviewQueueKind, number>>;
  summary?: ReviewQueueSummary | null;
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

/* ---------- product photos, barcodes, «Больше не покупать» (stage 2) ---------- */

export type BarcodeSource = "scan" | "manual" | "openFoodFacts";

export interface ProductPhotoInfo {
  id: string;
  createdAt: string;
  width: number;
  height: number;
  /** Bytes of the stored copy. */
  size: number;
  isMain: boolean;
  url: string;
  thumbnailUrl: string;
}

export interface ProductBarcodeInfo {
  code: string;
  source: BarcodeSource;
  createdAt: string;
}

/** A product's photos (oldest first), barcodes and «Больше не покупать» mark, shared in the household. */
export interface ProductMediaResponse {
  productId: string;
  photos: ProductPhotoInfo[];
  mainPhotoId: string | null;
  barcodes: ProductBarcodeInfo[];
  avoid: { since: string } | null;
}

export interface ProductPhotoUploadResponse {
  photo: ProductPhotoInfo;
  /** False when this device photo (`clientId`) was uploaded before. */
  created: boolean;
  /** The product had no photos before this one (its name is read from it). */
  firstPhoto: boolean;
}

/** What Open Food Facts said about a barcode; a hint only, never trusted. */
export interface OffProductHint {
  name: string | null;
  brand: string | null;
  quantity: string | null;
  imageUrl: string | null;
}

export interface RememberedBarcodeResponse {
  code: string;
  rememberedAt: string;
  off: OffProductHint | null;
}

export interface BarcodeLookupResponse {
  /** The normalized code. */
  code: string;
  productIds: string[];
  remembered: RememberedBarcodeResponse | null;
}

export interface OpenFoodFactsResponse {
  status: "found" | "notFound" | "error";
  product: OffProductHint | null;
}
