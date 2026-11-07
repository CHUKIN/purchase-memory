import type {
  ProductNameLanguage,
  ProductNames,
  ProductPhotoNameResult,
  PhotoSearchResult,
  HaulPhotoPair,
  HaulPhotoProposal,
  HaulPhotoSaveResult,
  HaulPhotoStatus,
  PhotoSearchStatus,
  BrandSummary,
  AnalyticsAnswer,
  BarcodeLookupResponse,
  BarcodeSource,
  OffProductHint,
  OpenFoodFactsResponse,
  ProductMediaResponse,
  ProductPhotoUploadResponse,
  RememberedBarcodeResponse,
  AnalyticsAskResponse,
  AnalyticsContext,
  AnalyticsHistoryDetail,
  AnalyticsHistoryPage,
  AnalyticsPlan,
  CategorizationRunSummary,
  CreateProductRequest,
  Impression,
  ImpressionRating,
  LineApprovalChange,
  LineApprovalResult,
  ProductCatalogPage,
  ProductCategory,
  ProductChangeResult,
  ProductClassification,
  ProductDetail,
  ProductMergeResult,
  ProductTag,
  PurchaseDetail,
  PurchaseFeedPage,
  PurchaseReviewFilter,
  PurchaseView,
  RateablePurchaseItem,
  ReceiptReviewAuditEntry,
  ReceiptSnapshot,
  ReceiptUploadResult,
  ReceiptView,
  RecognitionStatus,
  ReviewQueue,
  ReviewQueueCount,
  TagSuggestionsResponse,
  TagSummary,
  UpdatePurchaseRequest
} from "./types";
import { authorize, reportSessionRejected } from "../auth/authSession";

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    /** From `Retry-After` on 429 responses. */
    public readonly retryAfterSeconds: number | null = null,
    /** "pending" or "blocked" when the server refused a not-approved user. */
    public readonly accessStatus: string | null = null,
    /** Machine-readable `code` of a problem response, e.g. "analyticsDisabled". */
    public readonly code: string | null = null,
    /** On a 409 from creating a product: the product that already carries the name. */
    public readonly existingProductId: string | null = null
  ) {
    super(message);
  }
}

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const { init: authorized, token } = await authorize(init);
  const response = await fetch(url, authorized);
  if (response.ok) {
    return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
  }

  let message = `Запрос завершился с ошибкой ${response.status}.`;
  let accessStatus: string | null = null;
  let code: string | null = null;
  let existingProductId: string | null = null;
  try {
    const problem = (await response.json()) as {
      detail?: string;
      title?: string;
      accessStatus?: string;
      code?: string;
      existingProductId?: string;
    };
    message = problem.detail ?? problem.title ?? message;
    accessStatus = problem.accessStatus ?? null;
    code = problem.code ?? null;
    existingProductId = problem.existingProductId ?? null;
  } catch {
    // The server may return an empty or non-JSON response.
  }

  if (response.status === 401 || (response.status === 403 && accessStatus)) {
    reportSessionRejected(response.status, token);
  }

  const retryAfter = Number(response.headers?.get?.("Retry-After"));
  throw new ApiError(
    message,
    response.status,
    Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter : null,
    accessStatus,
    code,
    existingProductId
  );
}

export interface AuthConfig {
  mode: "supabase" | "developmentBypass" | "unconfigured";
  supabaseUrl: string | null;
  supabasePublishableKey: string | null;
  providers: string[];
  developmentBypass: boolean;
}

export type AccessStatus = "pending" | "approved" | "blocked";

export interface CurrentUser {
  id: string;
  email: string | null;
  status: AccessStatus;
}

/** Public: the login screen needs it before anyone is signed in. */
export async function getAuthConfig(): Promise<AuthConfig> {
  const response = await fetch("/api/auth/config");
  if (!response.ok) throw new ApiError(`Запрос завершился с ошибкой ${response.status}.`, response.status);
  return (await response.json()) as AuthConfig;
}

export const getCurrentUser = () => request<CurrentUser>("/api/me");

/** Fetches a protected image with the session token and returns its bytes. */
export async function fetchProtectedBlob(url: string, signal?: AbortSignal): Promise<Blob> {
  const { init: authorized, token } = await authorize({ signal });
  const response = await fetch(url, authorized);
  if (!response.ok) {
    if (response.status === 401) reportSessionRejected(401, token);
    throw new ApiError(`Запрос завершился с ошибкой ${response.status}.`, response.status);
  }
  return response.blob();
}

export const getRecognitionStatus = () => request<RecognitionStatus>("/api/recognition/status");

/**
 * `process: true` queues recognition in the same request, so the receipt
 * spends one upload-limit token instead of two (upload, then `/process`).
 */
export async function uploadReceipt(files: File[], options: { process?: boolean } = {}): Promise<ReceiptUploadResult> {
  const form = new FormData();
  files.forEach((file) => form.append("images", file));
  const url = options.process ? "/api/receipts?process=true" : "/api/receipts";
  return request<ReceiptUploadResult>(url, { method: "POST", body: form });
}

export const processReceipt = (receiptId: string) =>
  request<ReceiptView>(`/api/receipts/${receiptId}/process`, { method: "POST" });

export const getReceipt = (receiptId: string) =>
  request<ReceiptView>(`/api/receipts/${receiptId}`);

export const listRecentReceipts = (limit = 20) =>
  request<ReceiptView[]>(`/api/receipts?limit=${limit}`);

/** The caller's own receipts from the last 24 hours that are not saved yet, newest first. */
export const listMyUnsavedReceipts = () =>
  request<ReceiptView[]>("/api/receipts/my-unsaved");

export const reviewReceipt = (receiptId: string, snapshot: ReceiptSnapshot) =>
  request<ReceiptView>(`/api/receipts/${receiptId}/review`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(snapshot)
  });

export const saveReceipt = (receiptId: string) =>
  request<PurchaseView>(`/api/receipts/${receiptId}/save`, { method: "POST" });

export const getReceiptAuditTrail = (receiptId: string) =>
  request<ReceiptReviewAuditEntry[]>(`/api/receipts/${receiptId}/audit`);

export interface PurchaseFeedRequest {
  query?: string;
  /** Inclusive local calendar day, `YYYY-MM-DD`. */
  from?: string;
  /** Inclusive local calendar day, `YYYY-MM-DD`. */
  to?: string;
  status?: PurchaseReviewFilter;
  /** A category id, or `"none"` for purchases with a product without a category. A group includes its subcategories. */
  category?: string;
  brand?: string;
  /** A tag id. */
  tag?: string;
  page?: number;
  pageSize?: number;
}

/** Shared by the feed and the catalog: the same wire format for the same filter. */
function setClassificationParameters(parameters: URLSearchParams, category?: string, brand?: string, tag?: string) {
  if (category === "none") parameters.set("uncategorized", "true");
  else if (category) parameters.set("categoryId", category);
  const cleanBrand = brand?.trim();
  if (cleanBrand) parameters.set("brand", cleanBrand);
  if (tag) parameters.set("tagId", tag);
}

/**
 * The date inputs are local calendar days, so they are widened to the whole
 * local day before being sent as instants the backend can compare.
 */
function dayStart(day: string): string {
  return new Date(`${day}T00:00:00`).toISOString();
}

function dayEnd(day: string): string {
  return new Date(`${day}T23:59:59.999`).toISOString();
}

export async function getPurchaseFeed(request_: PurchaseFeedRequest = {}): Promise<PurchaseFeedPage> {
  const parameters = new URLSearchParams();
  const query = request_.query?.trim();
  if (query) parameters.set("q", query);
  if (request_.from) parameters.set("from", dayStart(request_.from));
  if (request_.to) parameters.set("to", dayEnd(request_.to));
  if (request_.status && request_.status !== "all") parameters.set("status", request_.status);
  setClassificationParameters(parameters, request_.category, request_.brand, request_.tag);
  if (request_.page && request_.page !== 1) parameters.set("page", String(request_.page));
  if (request_.pageSize) parameters.set("pageSize", String(request_.pageSize));
  const suffix = parameters.size ? `?${parameters}` : "";
  return request<PurchaseFeedPage>(`/api/purchases${suffix}`);
}

export const getPurchase = (purchaseId: string) =>
  request<PurchaseDetail>(`/api/purchases/${encodeURIComponent(purchaseId)}`);

export const updatePurchase = (purchaseId: string, body: UpdatePurchaseRequest) =>
  request<PurchaseDetail>(`/api/purchases/${encodeURIComponent(purchaseId)}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body)
  });

/**
 * Sends a batch of swipe-review marks. `keepalive` lets the request finish
 * while the page is being hidden or closed.
 */
export const postLineApprovals = (items: LineApprovalChange[], options: { keepalive?: boolean } = {}) =>
  request<{ items: LineApprovalResult[] }>("/api/purchases/line-approvals", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ items }),
    keepalive: options.keepalive ?? false
  });

export interface ProductCatalogRequest {
  query?: string;
  /** A category id, or `"none"` for products without a category. A group includes its subcategories. */
  category?: string;
  brand?: string;
  /** A tag id. */
  tag?: string;
  /** Only products flagged «проверить». */
  needsReview?: boolean;
  /** Leaves this product out (the «Объединить с…» picker). */
  excludeProductId?: string;
  page?: number;
  pageSize?: number;
}

export async function getProductCatalog(request_: ProductCatalogRequest = {}): Promise<ProductCatalogPage> {
  const parameters = new URLSearchParams();
  const query = request_.query?.trim();
  if (query) parameters.set("q", query);
  setClassificationParameters(parameters, request_.category, request_.brand, request_.tag);
  if (request_.needsReview) parameters.set("needsReview", "true");
  if (request_.excludeProductId) parameters.set("excludeProductId", request_.excludeProductId);
  if (request_.page && request_.page !== 1) parameters.set("page", String(request_.page));
  if (request_.pageSize) parameters.set("pageSize", String(request_.pageSize));
  const suffix = parameters.size ? `?${parameters}` : "";
  return request<ProductCatalogPage>(`/api/products${suffix}`);
}

export const getProduct = (productId: string) =>
  request<ProductDetail>(`/api/products/${encodeURIComponent(productId)}`);

export const listCategories = () => request<ProductCategory[]>("/api/categories");

export const listBrands = () => request<BrandSummary[]>("/api/brands");

export const listTags = () => request<TagSummary[]>("/api/tags");

const jsonBody = (method: string, body: unknown): RequestInit => ({
  method,
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify(body)
});

/** A product made by hand or from a scanned barcode; a namesake answers 409 with `existingProductId`. */
export const createProduct = (body: CreateProductRequest) =>
  request<ProductChangeResult>("/api/products", jsonBody("POST", body));

export const renameProduct = (productId: string, name: string) =>
  request<ProductChangeResult>(`/api/products/${encodeURIComponent(productId)}/name`, jsonBody("PUT", { name }));

/** Sets one human name (confirmed); an empty name clears it (the receipt text shows). */
export const setProductName = (productId: string, language: ProductNameLanguage, name: string) =>
  request<ProductNames>(`/api/products/${encodeURIComponent(productId)}/name`, jsonBody("PUT", { name, language }));

/**
 * «Верно» for the names: every suggested name becomes confirmed as it is. The
 * names on screen go along; if the product's names changed meanwhile (a new
 * suggestion), the server confirms nothing and answers 409.
 */
export const confirmProductNames = (productId: string, shown: { namePl: string | null; nameRu: string | null }) =>
  request<ProductNames>(`/api/products/${encodeURIComponent(productId)}/names/confirm`, jsonBody("POST", shown));

/**
 * «Предложить заново»: asks the model again (paid, per-user limit). Confirmed
 * names never change. 503 (`nameSuggestionsUnavailable`) and 429 are expected
 * answers, not crashes.
 */
export const suggestProductNames = (productId: string) =>
  request<ProductNames>(`/api/products/${encodeURIComponent(productId)}/name-suggestions`, { method: "POST" });

/**
 * «Название по фото»: one compressed package photo (JPEG, at most 1.5 MB) is
 * read by a paid vision model (per-user limit). The server keeps no copy.
 * Suggested or missing names are replaced, a person's names only proposed.
 * 503 (`nameFromPhotoUnavailable`), 429, 413 and 415 are expected answers.
 */
export function nameProductFromPhoto(productId: string, photo: Blob): Promise<ProductPhotoNameResult> {
  const form = new FormData();
  form.append("photo", photo, "photo.jpg");
  return request<ProductPhotoNameResult>(`/api/products/${encodeURIComponent(productId)}/name-from-photo`, { method: "POST", body: form });
}

/** Whether «Найти по фото» is switched on and configured on the server. Free. */
export const getPhotoSearchStatus = () => request<PhotoSearchStatus>("/api/products/find-by-photo/status");

export const getHaulPhotoStatus = () => request<HaulPhotoStatus>("/api/purchases/haul-photo/status");

/**
 * «Сфотографировать покупки», step 1: one photo of the purchase's items
 * (multipart field «photo») → a box and a proposed line per item. Nothing is
 * stored. 503 (`haulPhotoDisabled` or `haulPhotoUnavailable`), 404, 429, 413
 * and 415 are expected answers.
 */
export function proposeHaulPhoto(purchaseId: string, photo: Blob): Promise<HaulPhotoProposal> {
  const form = new FormData();
  form.append("photo", photo, "photo.jpg");
  return request<HaulPhotoProposal>(`/api/purchases/${encodeURIComponent(purchaseId)}/haul-photo`, { method: "POST", body: form });
}

/**
 * Step 2: the same photo again with the confirmed pairs; each crop becomes a
 * photo of its line's product. Every pair gets its own outcome; repeating the
 * request adds nothing twice.
 */
export function saveHaulPhoto(purchaseId: string, photo: Blob, pairs: HaulPhotoPair[]): Promise<HaulPhotoSaveResult> {
  const form = new FormData();
  form.append("photo", photo, "photo.jpg");
  form.append("pairs", JSON.stringify(pairs));
  return request<HaulPhotoSaveResult>(`/api/purchases/${encodeURIComponent(purchaseId)}/haul-photo/save`, { method: "POST", body: form });
}

/**
 * «Найти по фото»: one compressed photo (multipart field «photo»), up to three
 * of the household's products it most likely shows. 503 (`findByPhotoDisabled`
 * or `findByPhotoUnavailable`), 429, 413 and 415 are expected answers.
 */
export function findProductsByPhoto(photo: Blob): Promise<PhotoSearchResult> {
  const form = new FormData();
  form.append("photo", photo, "photo.jpg");
  return request<PhotoSearchResult>("/api/products/find-by-photo", { method: "POST", body: form });
}

/**
 * «Вернуть» after a photo replaced a name: puts `previousName` back (null: no
 * name, the receipt text shows). 409 when the name changed since.
 */
export const undoPhotoName = (productId: string, change: { language: ProductNameLanguage; photoName: string; previousName: string | null }) =>
  request<ProductNames>(`/api/products/${encodeURIComponent(productId)}/names/undo-photo`, jsonBody("POST", change));

/** Moves everything of `productId` into `targetProductId` and deletes it. No undo. */
export const mergeProduct = (productId: string, targetProductId: string) =>
  request<ProductMergeResult>(
    `/api/products/${encodeURIComponent(productId)}/merge-into/${encodeURIComponent(targetProductId)}`,
    { method: "POST" }
  );

/** «Это другой товар»: clears the «проверить» flag and keeps the product as it is. */
export const confirmDistinctProduct = (productId: string) =>
  request<ProductChangeResult>(`/api/products/${encodeURIComponent(productId)}/confirm-distinct`, { method: "POST" });

export const createCategory = (name: string, parentId: string | null) =>
  request<ProductCategory>("/api/categories", jsonBody("POST", { name, parentId }));

export const renameCategory = (categoryId: string, name: string) =>
  request<ProductCategory>(`/api/categories/${encodeURIComponent(categoryId)}`, jsonBody("PUT", { name }));

export const deleteCategory = (categoryId: string) =>
  request<{ categoryId: string; movedToCategoryId: string | null; movedProductCount: number }>(
    `/api/categories/${encodeURIComponent(categoryId)}`,
    { method: "DELETE" }
  );

export const createTag = (name: string) => request<TagSummary>("/api/tags", jsonBody("POST", { name }));

export const renameTag = (tagId: string, name: string) =>
  request<TagSummary>(`/api/tags/${encodeURIComponent(tagId)}`, jsonBody("PUT", { name }));

export const deleteTag = (tagId: string) =>
  request<{ tagId: string; unlinkedProductCount: number }>(
    `/api/tags/${encodeURIComponent(tagId)}`,
    { method: "DELETE" }
  );

/** Replaces the product's tags with exactly these. */
export const setProductTags = (productId: string, tagIds: string[]) =>
  request<{ productId: string; tags: ProductTag[] }>(
    `/api/products/${encodeURIComponent(productId)}/tags`,
    jsonBody("PUT", { tagIds })
  );

/**
 * Asks the AI which existing household tags fit the product. Only suggests:
 * accepting goes through `setProductTags`. 503 (`tagSuggestionsUnavailable`)
 * and 429 are expected answers, not crashes.
 */
export const suggestProductTags = (productId: string, locale: string) =>
  request<TagSuggestionsResponse>(
    `/api/products/${encodeURIComponent(productId)}/tag-suggestions?${new URLSearchParams({ locale })}`,
    { method: "POST" }
  );

export interface ClassifyProductRequest {
  /** Present to change the category; `null` confirms «Без категории». */
  categoryId?: string | null;
  /** Present to change the brand; `null` or empty clears it. */
  brand?: string | null;
}

/** A person's decision: whatever is sent becomes confirmed; absent fields stay untouched. */
export const classifyProduct = (productId: string, change: ClassifyProductRequest) =>
  request<ProductClassification>(
    `/api/products/${encodeURIComponent(productId)}/classification`,
    jsonBody("PUT", {
      setCategory: "categoryId" in change,
      categoryId: change.categoryId ?? null,
      setBrand: "brand" in change,
      brand: change.brand ?? null
    })
  );


export const getAnalyticsContext = () => request<AnalyticsContext>("/api/analytics/context");

/**
 * Asks a question (rate limited per user). `previousPlan` makes it a follow-up that keeps those filters.
 * A question asked before reuses its saved plan without the model, unless `askModel` is set («Спросить заново»).
 */
export const askAnalytics = (question: string, previousPlan: AnalyticsPlan | null, locale: string, askModel = false) =>
  request<AnalyticsAskResponse>(
    "/api/analytics/ask",
    jsonBody("POST", askModel ? { question, previousPlan, locale, askModel } : { question, previousPlan, locale })
  );

/** The household's previous questions, newest answer first. */
export const listAnalyticsHistory = (limit = 20, offset = 0) =>
  request<AnalyticsHistoryPage>(`/api/analytics/history?${new URLSearchParams({ limit: String(limit), offset: String(offset) })}`);

export const getAnalyticsHistoryEntry = (entryId: string) =>
  request<AnalyticsHistoryDetail>(`/api/analytics/history/${encodeURIComponent(entryId)}`);

/** Recalculates a saved question's plan on today's data, without any model call. */
export const refreshAnalyticsHistoryEntry = (entryId: string, locale: string) =>
  request<AnalyticsHistoryDetail>(
    `/api/analytics/history/${encodeURIComponent(entryId)}/refresh?${new URLSearchParams({ locale })}`,
    { method: "POST" }
  );

export const deleteAnalyticsHistoryEntry = (entryId: string) =>
  request<void>(`/api/analytics/history/${encodeURIComponent(entryId)}`, { method: "DELETE" });

/** Runs an edited plan without any model call. */
export const runAnalyticsPlan = (plan: AnalyticsPlan, locale: string) =>
  request<AnalyticsAnswer>("/api/analytics/run", jsonBody("POST", { plan, locale }));

export interface ReviewQueueFilter {
  /** A category id (a group includes its subcategories), or `"none"` for «Без категории». */
  category?: string;
  /** A tag id. */
  tag?: string;
}

/**
 * The queue, optionally narrowed to items about products in a category and/or
 * with a tag (the same wire format as the feed); the badge count is always the whole queue.
 */
export function getReviewQueue(limitPerKind = 20, filter: ReviewQueueFilter = {}): Promise<ReviewQueue> {
  const parameters = new URLSearchParams({ limitPerKind: String(limitPerKind) });
  setClassificationParameters(parameters, filter.category, undefined, filter.tag);
  return request<ReviewQueue>(`/api/review-queue?${parameters}`);
}

export const getReviewQueueCount = () => request<ReviewQueueCount>("/api/review-queue/count");

export interface CreateImpressionRequest {
  purchaseItemId: string;
  rating: ImpressionRating;
  note?: string;
  photo?: File | null;
}

/** Multipart, so an optional photo travels in the same request as the rating. */
export function createImpression(request_: CreateImpressionRequest): Promise<Impression> {
  const form = new FormData();
  form.set("purchaseItemId", request_.purchaseItemId);
  form.set("rating", request_.rating);
  const note = request_.note?.trim();
  if (note) form.set("note", note);
  if (request_.photo) form.set("photo", request_.photo);
  return request<Impression>("/api/impressions", { method: "POST", body: form });
}

/** Withdraws one of the caller's own impressions; the server refuses anyone else's. */
export const deleteImpression = (impressionId: string) =>
  request<void>(`/api/impressions/${encodeURIComponent(impressionId)}`, { method: "DELETE" });

export const listProductImpressions = (productId: string) =>
  request<Impression[]>(`/api/products/${encodeURIComponent(productId)}/impressions`);

export const listProductRateableLines = (productId: string) =>
  request<RateablePurchaseItem[]>(`/api/products/${encodeURIComponent(productId)}/rateable-lines`);

export const listPurchaseImpressions = (purchaseId: string) =>
  request<Impression[]>(`/api/purchases/${encodeURIComponent(purchaseId)}/impressions`);

export const listUnratedPurchaseItems = (limit = 6) =>
  request<RateablePurchaseItem[]>(`/api/impressions/unrated?limit=${limit}`);

/* ---------- product photos, barcodes, «Больше не покупать», remembered codes ---------- */

const productPath = (productId: string) => `/api/products/${encodeURIComponent(productId)}`;

export const getProductMedia = (productId: string) =>
  request<ProductMediaResponse>(`${productPath(productId)}/media`);

/**
 * One compressed photo (JPEG, at most 1.5 MB). `clientId` (a device photo id)
 * makes a repeated upload return the stored photo instead of a second copy.
 * 413, 415 and 409 (`photoLimit`) are expected answers.
 */
export function uploadProductPhoto(productId: string, photo: Blob, clientId?: string): Promise<ProductPhotoUploadResponse> {
  const form = new FormData();
  form.append("photo", photo, "photo.jpg");
  if (clientId) form.append("clientId", clientId);
  return request<ProductPhotoUploadResponse>(`${productPath(productId)}/photos`, { method: "POST", body: form });
}

export const deleteProductPhoto = (productId: string, photoId: string) =>
  request<void>(`${productPath(productId)}/photos/${encodeURIComponent(photoId)}`, { method: "DELETE" });

export const setMainProductPhoto = (productId: string, photoId: string) =>
  request<ProductMediaResponse>(`${productPath(productId)}/photos/${encodeURIComponent(photoId)}/main`, { method: "PUT" });

export const productPhotoUrl = (photoId: string, variant: "photo" | "thumbnail" = "photo") =>
  `/api/product-photos/${encodeURIComponent(photoId)}${variant === "thumbnail" ? "/thumbnail" : ""}`;

/** 409 `barcodeTaken` (with `existingProductId`) when another product has the code. */
export const addProductBarcode = (productId: string, code: string, source: BarcodeSource) =>
  request<{ barcode: { code: string; source: BarcodeSource; createdAt: string }; created: boolean }>(
    `${productPath(productId)}/barcodes`,
    jsonBody("POST", { code, source })
  );

export const deleteProductBarcode = (productId: string, code: string) =>
  request<void>(`${productPath(productId)}/barcodes/${encodeURIComponent(code)}`, { method: "DELETE" });

/** `since` keeps the date of a mark moved from a phone. */
export const setProductAvoid = (productId: string, avoid: boolean, since?: string) =>
  request<ProductMediaResponse>(`${productPath(productId)}/avoid`, jsonBody("PUT", since ? { avoid, since } : { avoid }));

export const lookupBarcode = (code: string) =>
  request<BarcodeLookupResponse>(`/api/barcodes/${encodeURIComponent(code)}`);

export const listRememberedBarcodes = () => request<RememberedBarcodeResponse[]>("/api/remembered-barcodes");

/** 409 `barcodeTaken` when the code already belongs to a product. */
export const rememberBarcode = (code: string, off: OffProductHint | null) =>
  request<RememberedBarcodeResponse>(`/api/remembered-barcodes/${encodeURIComponent(code)}`, jsonBody("PUT", { off }));

export const forgetRememberedBarcode = (code: string) =>
  request<void>(`/api/remembered-barcodes/${encodeURIComponent(code)}`, { method: "DELETE" });

/** Through our server, which sends Open Food Facts only the barcode; «error» means it did not answer. */
export const lookupOpenFoodFactsProduct = (code: string, signal?: AbortSignal) =>
  request<OpenFoodFactsResponse>(`/api/open-food-facts/${encodeURIComponent(code)}`, { signal });
